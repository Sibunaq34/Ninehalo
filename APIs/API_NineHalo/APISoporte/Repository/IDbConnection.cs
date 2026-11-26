using System.Data;

namespace APINineHalo.Repository
{
    public interface IDbConnectionF
    {
        IDbConnection CreateConnection();
    }
}
