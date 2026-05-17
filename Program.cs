using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using BleachedForums; // Imports your DatabaseConnection namespace

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// Add support for session state (keeps track of who is logged in)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Enable session support
app.UseSession();

// Serve files from 'wwwroot'
app.UseStaticFiles();

// Set login as the default landing page
DefaultFilesOptions defaultOptions = new DefaultFilesOptions();
defaultOptions.DefaultFileNames.Clear();
defaultOptions.DefaultFileNames.Add("login");
app.UseDefaultFiles(defaultOptions);

// ==========================================
// 1. USER AUTHENTICATION ENDPOINT (LOGIN)
// ==========================================
app.MapPost("/api/auth/login", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    // Fix Null Warnings
    string username = form["username"].ToString() ?? string.Empty;
    string password = form["password"].ToString() ?? string.Empty;

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
    {
        return Results.BadRequest(new { message = "Username and password are required." });
    }

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            // Open the connection (Mandatory for MySQL)
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            string query = "SELECT userID, username, passwordHash, status, role FROM users WHERE username = @username LIMIT 1;";
            
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@username", username);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        // Fix "String to Int" conversion errors using Indexer + ToString
                        string dbPasswordHash = reader["passwordHash"]?.ToString() ?? "";
                        string status = reader["status"]?.ToString() ?? "";
                        string role = reader["role"]?.ToString() ?? "";
                        int userID = Convert.ToInt32(reader["userID"]);

                        if (status.ToLower() == "suspended" || status.ToLower() == "banned")
                        {
                            return Results.Json(new { success = false, message = $"Your account is {status}." }, statusCode: 403);
                        }

                        if (password == dbPasswordHash)
                        {
                            context.Session.SetString("Username", username);
                            context.Session.SetString("Role", role);
                            context.Session.SetInt32("UserID", userID);

                            return Results.Ok(new { success = true, redirectUrl = "dashboard" });
                        }
                    }
                }
            }
        }
        return Results.Json(new { success = false, message = "Invalid username or password." }, statusCode: 401);
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = $"Error: {ex.Message}" }, statusCode: 500);
    }
});

// ==========================================
// 2. PASSWORD RECOVERY ENDPOINT (RESET)
// ==========================================
app.MapPost("/api/auth/reset", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string identifier = form["identifier"]; // Can be Username or Email
    string newPassword = form["newPassword"];

    if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(newPassword))
    {
        return Results.BadRequest(new { message = "All fields are required." });
    }

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            // Query checks if user exists before attempting update
            string checkQuery = "SELECT userID FROM users WHERE username = @id OR email = @id LIMIT 1;";
            bool userExists = false;

            using (MySqlCommand checkCmd = new MySqlCommand(checkQuery, conn))
            {
                checkCmd.Parameters.AddWithValue("@id", identifier);
                using (var reader = await checkCmd.ExecuteReaderAsync())
                {
                    if (reader.HasRows) userExists = true;
                }
            }

            if (!userExists)
            {
                return Results.Json(new { success = false, message = "No matching account found with that username or email." }, statusCode: 404);
            }

            // Execute actual password update 
            string updateQuery = "UPDATE users SET passwordHash = @newPassword WHERE username = @id OR email = @id;";
            using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
            {
                updateCmd.Parameters.AddWithValue("@newPassword", newPassword); // Directly update plain-text (or hash if using hashing library)
                updateCmd.Parameters.AddWithValue("@id", identifier);

                await updateCmd.ExecuteNonQueryAsync();
            }
        }

        return Results.Ok(new { success = true, message = "Password updated successfully!" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { success = false, message = $"Failed to update password: {ex.Message}" }, statusCode: 500);
    }
});

// ==========================================
// 3. USER MANAGEMENT ENDPOINTS
// ==========================================

// GET ALL USERS (Search & List)
app.MapGet("/api/users", async () =>
{
    var users = new List<object>();
    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            await conn.OpenAsync();
            string query = "SELECT userID, username, email, role, status FROM users";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    users.Add(new {
                        userID = reader["userID"], // Let JSON handle the type
                        username = reader["username"].ToString(),
                        email = reader["email"].ToString(),
                        role = reader["role"].ToString(),
                        status = reader["status"].ToString()
                    });
                }
            }
        }
        return Results.Ok(users);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// ADD NEW USER
app.MapPost("/api/users/add", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            //await conn.OpenAsync();
            string query = "INSERT INTO users (username, email, passwordHash, role, status) VALUES (@u, @e, @p, @r, @s)";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@u", form["username"].ToString());
                cmd.Parameters.AddWithValue("@e", form["email"].ToString());
                cmd.Parameters.AddWithValue("@p", form["password"].ToString());
                cmd.Parameters.AddWithValue("@r", form["role"].ToString());
                cmd.Parameters.AddWithValue("@s", form["status"].ToString());
                await cmd.ExecuteNonQueryAsync();
            }
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

// UPDATE USER DETAILS
app.MapPost("/api/users/update", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            //await conn.OpenAsync();
            string query = "UPDATE users SET username=@u, email=@e, role=@r, status=@s WHERE userID=@id";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@u", form["username"].ToString());
                cmd.Parameters.AddWithValue("@e", form["email"].ToString());
                cmd.Parameters.AddWithValue("@r", form["role"].ToString());
                cmd.Parameters.AddWithValue("@s", form["status"].ToString());
                cmd.Parameters.AddWithValue("@id", form["userId"].ToString());
                await cmd.ExecuteNonQueryAsync();
            }
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapRazorPages();
app.Run();
