using Microsoft.AspNetCore.Mvc;

/* Schools EndPoint
 
   GET Get():
        Get List of used Schools.
        Input: {}
        RETURNS: { "Schools": [...] }
 */

namespace StudentExchangeBck
{
    [ApiController]
    [Route("[controller]")]
    public class Schools : ControllerBase
    {
        /// <summary>
        /// Get List of used Schools.
        /// Input: {}
        /// </summary>
        /// <returns>{ "Schools": [...] }</returns>
        [HttpGet]
        public Dictionary<string, object> Get()
        {
            // Get Schools from db
            List<object> schools = Sql.Read("SELECT [name] FROM Schools ORDER BY [name]")["name"];
            return new Dictionary<string, object>
            {
                { "Schools", schools }
            };
        }
    }
}
