using Microsoft.AspNetCore.Mvc;

namespace StudentExchangeBck
{
    public class AppAuthJson
    {
        public string? app_access { get; set; }

        public static IActionResult? Validate(AppAuthJson access)
        {
            if (access.app_access == null || access.app_access != Env._appAccess)
            {
                return new UnauthorizedObjectResult(new
                {
                    Message = "Not authorized."
                });
            }
            else return null;
        }
    }
}
