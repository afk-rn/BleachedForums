using System;
using MySql.Data.MySqlClient;

namespace BleachedForums
{
    /// <summary>
    /// A public utility class responsible for establishing and managing 
    /// the MySQL connection lifecycle for the Bleached Forums lab application.
    /// </summary>
    public class DatabaseConnection
    {
        // Connection parameters pointing to your local MySQL instance and "forumdb" database
        private static readonly string Server = "localhost";
        private static readonly string Database = "forumdb";
        private static readonly string Username = "root";
        private static readonly string Password = "root";
        private static readonly string Port = "3306";

        // Structured connection string construction
        private static readonly string ConnectionString = $"Server={Server};Port={Port};Database={Database};Uid={Username};Pwd={Password};";

        /// <summary>
        /// Public static method to get a new instance of an open MySQL connection.
        /// Ensure you wrap this in a "using" block on your backend pages to prevent connection leaks.
        /// </summary>
        /// <returns>An open MySqlConnection object ready to execute queries.</returns>
        public static MySqlConnection GetConnection()
        {
            MySqlConnection connection = new MySqlConnection(ConnectionString);
            
            try
            {
                connection.Open();
                return connection;
            }
            catch (MySqlException ex)
            {
                // Handle or log connection failures cleanly for lab environment debugging
                Console.WriteLine($"Database Connection Error: {ex.Message}");
                throw;
            }
        }
    }
}
