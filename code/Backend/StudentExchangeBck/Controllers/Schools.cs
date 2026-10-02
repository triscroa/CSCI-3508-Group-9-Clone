using Microsoft.AspNetCore.Mvc;

namespace StudentExchangeBck
{
    [ApiController]
    [Route("[controller]")]
    public class Schools : ControllerBase
    {
        [HttpGet]
        public Dictionary<string, object> Get()
        {
            List<object> schools = Sql.Read("SELECT [name] FROM Schools")["name"];
            return new Dictionary<string, object>
            {
                { "Schools", schools }
            };
        }
    }
}
