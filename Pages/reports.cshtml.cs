using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MySql.Data.MySqlClient;

namespace BleachedForums.Pages
{
    public class ReportsModel : PageModel
    {
        // Simple models easily structured for grid rendering or future Excel exportation
        public class SupportTicketSummary
        {
            public int TicketID { get; set; }
            public string Type { get; set; } = string.Empty;
            public string Priority { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
        }

        // Card Block Stats
        public int TotalUsers { get; set; }
        public int PendingTickets { get; set; }
        public int TotalThreads { get; set; } // Representing Total Threads/Posts in your schema
        public int LockedThreads { get; set; }  // Representing special moderation state ("Flagged/Locked")

        // Distribution Breakdown Indicators
        public int ActiveUsers { get; set; }
        public int SuspendedUsers { get; set; }
        public int BannedUsers { get; set; }

        // Core Data Grid Source
        public List<SupportTicketSummary> RecentTickets { get; set; } = new List<SupportTicketSummary>();

        public void OnGet()
        {
            LoadReportStatistics();
        }

        private void LoadReportStatistics()
        {
            using (MySqlConnection conn = DatabaseConnection.GetConnection())
            {
                // 1. Fetch Aggregated Metrics
                string countsQuery = @"
                    SELECT 
                        (SELECT COUNT(*) FROM users) as total_u,
                        (SELECT COUNT(*) FROM supporttickets WHERE status = 'open' OR status = 'in_progress') as pending_t,
                        (SELECT COUNT(*) FROM threads) as total_th,
                        (SELECT COUNT(*) FROM threads WHERE status = 'locked' OR status = 'hidden') as flagged_th,
                        (SELECT COUNT(*) FROM users WHERE LOWER(status) = 'active') as active_u,
                        (SELECT COUNT(*) FROM users WHERE LOWER(status) = 'suspended') as susp_u,
                        (SELECT COUNT(*) FROM users WHERE LOWER(status) = 'banned') as ban_u;";

                using (MySqlCommand cmd = new MySqlCommand(countsQuery, conn))
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        TotalUsers = Convert.ToInt32(reader["total_u"]);
                        PendingTickets = Convert.ToInt32(reader["pending_t"]);
                        TotalThreads = Convert.ToInt32(reader["total_th"]);
                        LockedThreads = Convert.ToInt32(reader["flagged_th"]);
                        ActiveUsers = Convert.ToInt32(reader["active_u"]);
                        SuspendedUsers = Convert.ToInt32(reader["susp_u"]);
                        BannedUsers = Convert.ToInt32(reader["ban_u"]);
                    }
                }
            }

            using (MySqlConnection conn = DatabaseConnection.GetConnection())
            {
                // 2. Fetch Data Grid Collection (Limiting to top 10 for quick diagnostic layout display)
                string gridQuery = "SELECT ticketID, type, priority, status FROM supporttickets ORDER BY ticketID DESC LIMIT 10;";
                
                using (MySqlCommand cmd = new MySqlCommand(gridQuery, conn))
                using (MySqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        RecentTickets.Add(new SupportTicketSummary
                        {
                            TicketID = reader.GetInt32("ticketID"),
                            Type = reader.GetString("type"),
                            Priority = reader.GetString("priority"),
                            Status = reader.GetString("status")
                        });
                    }
                }
            }
        }
    }
}