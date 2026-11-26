using System.Data;
using MySql.Data.MySqlClient;


namespace APINineHalo.Repository
{    
    
    public class DbConnectionFactory : IDbConnectionF
    {
        private readonly IConfiguration _configuration;

        public DbConnectionFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection CreateConnection()
        {
            return new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        }
    }
}