using Microsoft.AspNetCore.Mvc.RazorPages;
using MySql.Data.MySqlClient;
using BleachedForums;

public class UserManagementModel : PageModel
{
    public List<UserDto> Users { get; set; } = new();

    public async Task OnGetAsync()
    {
        using (var conn = DatabaseConnection.GetConnection())
        {
            var query = "SELECT userID, username, email, role, status FROM users";
            using (var cmd = new MySqlCommand(query, conn))
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    Users.Add(new UserDto {
    			UserID = Convert.ToInt32(reader["userID"]),
    			Username = reader["username"].ToString() ?? "",
    			Email = reader["email"].ToString() ?? "",
    			Role = reader["role"].ToString() ?? "",
    			Status = reader["status"].ToString() ?? ""
		    });
                }
            }
        }
    }
}

public class UserDto {
    public int UserID { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "";
    public string Status { get; set; } = "";
}