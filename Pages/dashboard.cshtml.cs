using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySql.Data.MySqlClient;

namespace BleachedForums.Pages
{
    public class DashboardModel : PageModel
    {
        // Models mapped directly to your MySQL table schemas
        public class ForumThread
        {
            public int ThreadID { get; set; }
            public int CategoryID { get; set; }
            public int AuthorID { get; set; }
            public string Title { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; }
            public bool IsPinned { get; set; }
            public string Status { get; set; } = "open";
            public DateTime? RecentActivityAt { get; set; }
        }

        public class SupportTicket
        {
            public int TicketID { get; set; }
            public int UserID { get; set; }
            public int? AdminID { get; set; }
            public string Type { get; set; } = "technical";
            public string Details { get; set; } = string.Empty;
            public string Status { get; set; } = "open";
            public string Priority { get; set; } = "medium";
        }

        // Exposed properties for the view loops
        public List<ForumThread> Threads { get; set; } = new List<ForumThread>();
        public List<SupportTicket> Tickets { get; set; } = new List<SupportTicket>();

        public void OnGet()
        {
            LoadDashboardData();
        }

        private void LoadDashboardData()
        {
            // 1. Fetch Threads from MySQL Database
            using (MySqlConnection conn = DatabaseConnection.GetConnection())
            {
                string threadQuery = "SELECT threadID, categoryID, authorID, title, createdAt, isPinned, status, recentActivityAt FROM threads ORDER BY createdAt DESC;";
                using (MySqlCommand cmd = new MySqlCommand(threadQuery, conn))
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Threads.Add(new ForumThread
                        {
                            ThreadID = reader.GetInt32("threadID"),
                            CategoryID = reader.GetInt32("categoryID"),
                            AuthorID = reader.GetInt32("authorID"),
                            Title = reader.GetString("title"),
                            CreatedAt = reader.GetDateTime("createdAt"),
                            IsPinned = reader.GetByte("isPinned") == 1,
                            Status = reader.GetString("status"),
                            RecentActivityAt = reader.IsDBNull(reader.GetOrdinal("recentActivityAt")) ? null : reader.GetDateTime("recentActivityAt")
                        });
                    }
                }
            }

            // 2. Fetch Support Tickets from MySQL Database
            using (MySqlConnection conn = DatabaseConnection.GetConnection())
            {
                string ticketQuery = "SELECT ticketID, userID, adminID, type, details, status, priority FROM supporttickets ORDER BY ticketID DESC;";
                using (MySqlCommand cmd = new MySqlCommand(ticketQuery, conn))
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Tickets.Add(new SupportTicket
                        {
                            TicketID = reader.GetInt32("ticketID"),
                            UserID = reader.GetInt32("userID"),
                            AdminID = reader.IsDBNull(reader.GetOrdinal("adminID")) ? null : reader.GetInt32("adminID"),
                            Type = reader.GetString("type"),
                            Details = reader.GetString("details"),
                            Status = reader.GetString("status"),
                            Priority = reader.GetString("priority")
                        });
                    }
                }
            }
        }
    }
}