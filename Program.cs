using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;
using BleachedForums; // Imports your DatabaseConnection namespace
// Excel Function Dependencies
using OfficeOpenXml;
using OfficeOpenXml.Drawing.Chart;
using System.IO;
using System.Drawing;

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
            // Note: DatabaseConnection.GetConnection already runs connection.Open() synchronously.
            // If it isn't open, we ensure it's running here.
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            string query = "SELECT userID, username, passwordHash, status, role FROM users WHERE username = @username LIMIT 1;";
            
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@username", username);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
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
    string identifier = form["identifier"].ToString() ?? string.Empty;
    string newPassword = form["newPassword"].ToString() ?? string.Empty;

    if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(newPassword))
    {
        return Results.BadRequest(new { message = "All fields are required." });
    }

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

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

            string updateQuery = "UPDATE users SET passwordHash = @newPassword WHERE username = @id OR email = @id;";
            using (MySqlCommand updateCmd = new MySqlCommand(updateQuery, conn))
            {
                updateCmd.Parameters.AddWithValue("@newPassword", newPassword);
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

app.MapGet("/api/users", async () =>
{
    var users = new List<object>();
    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            string query = "SELECT userID, username, email, role, status FROM users";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    users.Add(new {
                        userID = reader["userID"],
                        username = reader["username"]?.ToString() ?? "",
                        email = reader["email"]?.ToString() ?? "",
                        role = reader["role"]?.ToString() ?? "",
                        status = reader["status"]?.ToString() ?? ""
                    });
                }
            }
        }
        return Results.Ok(users);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

app.MapPost("/api/users/add", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            
            string query = "INSERT INTO users (userID, username, email, passwordHash, role, status, createdAt, lastLogin) VALUES (@id, @u, @e, @p, @r, @s, @createdAt, @lastLogin)";
            
	    string idQuery = "SELECT userID FROM users ORDER BY userID desc LIMIT 1";
	    int id = 0;

	    using (MySqlCommand idCmd = new MySqlCommand(idQuery, conn))
            {
	    	var result = await idCmd.ExecuteScalarAsync();
            	if (result != null && result != DBNull.Value)
            	{
            	    id = Convert.ToInt32(result) + 1;
            	}
	    }

            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
		cmd.Parameters.AddWithValue("@id", id.ToString());
                cmd.Parameters.AddWithValue("@u", form["username"].ToString());
                cmd.Parameters.AddWithValue("@e", form["email"].ToString());
                cmd.Parameters.AddWithValue("@p", form["password"].ToString());
                cmd.Parameters.AddWithValue("@r", form["role"].ToString());
                cmd.Parameters.AddWithValue("@s", form["status"].ToString());
                
                cmd.Parameters.AddWithValue("@createdAt", DateTime.Now);
                cmd.Parameters.AddWithValue("@lastLogin", DateTime.Now);
                
                await cmd.ExecuteNonQueryAsync();
            }
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});
app.MapPost("/api/users/update", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
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

// ==========================================
// 4. NEW: DASHBOARD ACTION ENDPOINTS
// ==========================================

// ARCHIVE THREAD
app.MapPost("/api/threads/archive", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string threadId = form["threadId"].ToString() ?? string.Empty;

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            string query = "UPDATE threads SET status = 'archived' WHERE threadID = @id;";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", threadId);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

// TOGGLE THREAD PIN STATE
app.MapPost("/api/threads/toggle-pin", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string threadId = form["threadId"].ToString() ?? string.Empty;
    string currentPin = form["currentPin"].ToString() ?? string.Empty; // expects "true" or "false"
    
    int newPinValue = currentPin.ToLower() == "true" ? 0 : 1;

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            string query = "UPDATE threads SET isPinned = @pin WHERE threadID = @id;";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@pin", newPinValue);
                cmd.Parameters.AddWithValue("@id", threadId);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

// RESOLVE SUPPORT TICKET
app.MapPost("/api/tickets/resolve", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string ticketId = form["ticketId"].ToString() ?? string.Empty;

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            string query = "UPDATE supporttickets SET status = 'resolved' WHERE ticketID = @id;";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", ticketId);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

// GET SPECIFIC TICKET DETAILS
app.MapGet("/api/tickets/details", async (HttpContext context) =>
{
    string ticketIdStr = context.Request.Query["id"].ToString();

    if (string.IsNullOrEmpty(ticketIdStr) || !int.TryParse(ticketIdStr, out int ticketId))
    {
        return Results.BadRequest(new { message = "A valid Ticket ID is required." });
    }

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            
            // Query includes ticketID, userID, adminID, type, priority, status, and details
            string query = "SELECT ticketID, userID, adminID, type, priority, status, details FROM supporttickets WHERE ticketID = @id LIMIT 1;";
            
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@id", ticketId);

                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        var ticketDetails = new
                        {
                            ticketID = reader["ticketID"],
                            userID = reader["userID"],
                            // Handles potential null value if an admin isn't assigned yet
                            adminID = reader["adminID"] == DBNull.Value ? null : reader["adminID"],
                            type = reader["type"]?.ToString() ?? "",
                            priority = reader["priority"]?.ToString() ?? "",
                            status = reader["status"]?.ToString() ?? "",
                            details = reader["details"]?.ToString() ?? "No details provided."
                        };

                        return Results.Ok(ticketDetails);
                    }
                }
            }
        }
        return Results.NotFound(new { message = "Support ticket data profile not found." });
    }
    catch (Exception ex) 
    { 
        return Results.Json(new { message = ex.Message }, statusCode: 500); 
    }
});

// EXCEL EXPORT
app.MapGet("/api/reports/export", async (HttpContext context, string type) =>
{
    // The precise official EPPlus 8 syntax required for a non-commercial lab project
    OfficeOpenXml.ExcelPackage.License.SetNonCommercialOrganization("Bleached Forums Academic Lab");

    // Safe session username capture
    string generatedBy = context.Session?.GetString("Username") ?? "Authorized System Administrator";

    using (var package = new ExcelPackage())
    {
        // ---------------------------------------------------------------------
        // SHEET 1: DATA GRID CONTROL RECORD LISTING
        // ---------------------------------------------------------------------
        var dataSheet = package.Workbook.Worksheets.Add("Reported Data Grid");
        dataSheet.Cells.Style.Font.Name = "Segoe UI";

        // 1. Header Block (Company Name)
        dataSheet.Cells["A1"].Value = "BLEACHED FORUMS NETWORK CORP.";
        dataSheet.Cells["A1"].Style.Font.Size = 16;
        dataSheet.Cells["A1"].Style.Font.Bold = true;
        dataSheet.Cells["A1"].Style.Font.Color.SetColor(Color.FromArgb(30, 37, 43)); // Theme Dark

        dataSheet.Cells["A2"].Value = $"System Log Audit Template: {type.ToUpper()} REPORT";
        dataSheet.Cells["A2"].Style.Font.Size = 11;
        dataSheet.Cells["A2"].Style.Font.Italic = true;
        dataSheet.Cells["A2"].Style.Font.Color.SetColor(Color.Gray);

        // Logo positioning (Leaves space in columns E/F or rows 1-3)
	string webRootPath = builder.Environment.WebRootPath;
	string imagePath = Path.Combine(webRootPath, "images", "logo.jpg");
	
	if (File.Exists(imagePath))
{
    var picture = dataSheet.Drawings.AddPicture("CompanyLogo", imagePath);
    picture.SetPosition(0, 0, 4, 0);
    picture.SetSize(120, 50);
}
else
{
    dataSheet.Cells["E1"].Value = "[ Logo Asset Missing ]";
    dataSheet.Cells["E1"].Style.Font.Color.SetColor(Color.Red);
}

        // 2. Query Database Records dynamically depending on selected template type
        int startingRow = 5;
        string sqlQuery = "";

        if (type == "tickets")
        {
            sqlQuery = "SELECT ticketID as ID, type as Detail1, priority as Detail2, status as StatusText FROM supporttickets;";
            dataSheet.Cells["A4"].Value = "Ticket ID";
            dataSheet.Cells["B4"].Value = "Category Type";
            dataSheet.Cells["C4"].Value = "Priority Level";
            dataSheet.Cells["D4"].Value = "Current Status";
        }
        else if (type == "users")
        {
            sqlQuery = "SELECT userID as ID, username as Detail1, email as Detail2, status as StatusText FROM users;";
            dataSheet.Cells["A4"].Value = "User ID";
            dataSheet.Cells["B4"].Value = "Account Username";
            dataSheet.Cells["C4"].Value = "Email Address";
            dataSheet.Cells["D4"].Value = "Profile Status";
        }
        else // default fallback / threads template
	{
	    sqlQuery = "SELECT threadID as ID, title as Detail1, categoryID as Detail2, status as StatusText FROM threads;";
	    dataSheet.Cells["A4"].Value = "Thread ID";
	    dataSheet.Cells["B4"].Value = "Thread Title";
	    dataSheet.Cells["C4"].Value = "Category ID";
	    dataSheet.Cells["D4"].Value = "Moderation Status";
	}

        // Style the Table Data Grid Control Header row
        using (var range = dataSheet.Cells["A4:D4"])
        {
            range.Style.Font.Bold = true;
            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(30, 37, 43));
            range.Style.Font.Color.SetColor(Color.White);
        }

        int currentRow = startingRow;
        int statusCount1 = 0; // Counts used for generating our Chart metrics later
        int statusCount2 = 0;

        using (var conn = DatabaseConnection.GetConnection())
        {
            using (var cmd = new MySqlCommand(sqlQuery, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    dataSheet.Cells[$"A{currentRow}"].Value = reader.GetValue(0);
                    dataSheet.Cells[$"B{currentRow}"].Value = reader.GetString(1);
                    dataSheet.Cells[$"C{currentRow}"].Value = reader.GetValue(2);
                    
                    string statusVal = reader.GetString(3);
                    dataSheet.Cells[$"D{currentRow}"].Value = statusVal;

                    // Group summaries loosely to give the graph metric rows to bind onto
                    if (statusVal.ToLower() == "open" || statusVal.ToLower() == "active") statusCount1++;
                    else statusCount2++;

                    currentRow++;
                }
            }
        }

        // Auto-fit data columns cleanly
        dataSheet.Cells[dataSheet.Dimension.Address].AutoFitColumns();

        // 3. Signature Block Placeholder
        currentRow += 3;
        dataSheet.Cells[$"A{currentRow}"].Value = "Prepared & Verified By:";
        dataSheet.Cells[$"A{currentRow}"].Style.Font.Italic = true;
        
        currentRow += 2;
        dataSheet.Cells[$"A{currentRow}"].Value = "___________________________"; // Signature underline
        currentRow++;
        dataSheet.Cells[$"A{currentRow}"].Value = generatedBy; // Dynamic name string
        dataSheet.Cells[$"A{currentRow}"].Style.Font.Bold = true;
        currentRow++;
        dataSheet.Cells[$"A{currentRow}"].Value = $"Date: {DateTime.Now:MMMM dd, yyyy}";
        dataSheet.Cells[$"A{currentRow}"].Style.Font.Size = 9;


        // ---------------------------------------------------------------------
        // SHEET 2: ANALYTICS GRAPH GENERATION
        // ---------------------------------------------------------------------
        var graphSheet = package.Workbook.Worksheets.Add("Analytical Chart Visualization");

        // Set up a tiny table matrix in Sheet 2 to serve as the structural source for the chart engine
        graphSheet.Cells["A1"].Value = "Metric Metric Metric Metric Metric Metric Status Type";
        graphSheet.Cells["B1"].Value = "Aggregated Record Sum";
        
        graphSheet.Cells["A2"].Value = (type == "users") ? "Active Accounts" : "Open Status Metrics";
        graphSheet.Cells["B2"].Value = statusCount1;
        
        graphSheet.Cells["A3"].Value = (type == "users") ? "Suspended/Banned" : "Resolved/Closed Metrics";
        graphSheet.Cells["B3"].Value = statusCount2;

        // Build native MS Excel Pie Chart
        var pieChart = graphSheet.Drawings.AddChart("StatusDistributionChart", eChartType.Pie) as ExcelPieChart;
        pieChart.Title.Text = $"{type.ToUpper()} Status Share Distribution Matrix";
        pieChart.SetPosition(5, 0, 1, 0); // Position graph cleanly starting row 5 column B
        pieChart.SetSize(600, 400);

        // Bind the data series pathways accurately over the tracking metrics range rows
        var seriesPath = pieChart.Series.Add(graphSheet.Cells["B2:B3"], graphSheet.Cells["A2:A3"]);
        pieChart.DataLabel.ShowValue = true;
        pieChart.DataLabel.ShowPercent = true;

        // Return finished stream asset array package down binary delivery pipe pipeline directly
        var fileBytes = package.GetAsByteArray();
        string fileName = $"BleachedForums_{type}_Report_{DateTime.Now:yyyyMMdd}.xlsx";
        
        return Results.File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }
});

// GET ALL CATEGORIES (For filling the edit modal dropdown)
app.MapGet("/api/categories", async () =>
{
    var categories = new List<object>();
    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            string query = "SELECT categoryID, name FROM category WHERE status = 'open';";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    categories.Add(new {
                        categoryID = reader["categoryID"],
                        name = reader["name"]?.ToString() ?? ""
                    });
                }
            }
        }
        return Results.Ok(categories);
    }
    catch (Exception ex) { return Results.Problem(ex.Message); }
});

// UPDATE EXISTING THREAD
app.MapPost("/api/threads/update", async (HttpContext context) =>
{
    var form = await context.Request.ReadFormAsync();
    string threadId = form["threadId"].ToString();
    string title = form["title"].ToString();
    string categoryId = form["categoryId"].ToString();
    string status = form["status"].ToString();

    if (string.IsNullOrEmpty(threadId) || string.IsNullOrEmpty(title) || string.IsNullOrEmpty(categoryId))
    {
        return Results.BadRequest(new { message = "All thread attributes are required." });
    }

    try
    {
        using (MySqlConnection conn = DatabaseConnection.GetConnection())
        {
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
            string query = "UPDATE threads SET title = @title, categoryID = @catID, status = @status WHERE threadID = @id;";
            using (MySqlCommand cmd = new MySqlCommand(query, conn))
            {
                cmd.Parameters.AddWithValue("@title", title);
                cmd.Parameters.AddWithValue("@catID", Convert.ToInt32(categoryId));
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@id", threadId);
                await cmd.ExecuteNonQueryAsync();
            }
        }
        return Results.Ok(new { success = true });
    }
    catch (Exception ex) { return Results.BadRequest(new { message = ex.Message }); }
});

app.MapRazorPages();
app.Run();