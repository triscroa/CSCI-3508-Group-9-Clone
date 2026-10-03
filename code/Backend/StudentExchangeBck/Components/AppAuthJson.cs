using Microsoft.AspNetCore.Mvc;

namespace StudentExchangeBck
{
    public class AppAuthJson
    {
        public string? app_access { get; set; }
        public string? access_token { get; set; }

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

        public static IActionResult? ValidateWithToken(ControllerBase this_, AppAuthJson access, out object userId)
        {
            userId = null!;
            try
            {
                var msg = Validate(access);
                if (msg != null) return msg;

                if (string.IsNullOrWhiteSpace(access.access_token))
                    return new UnauthorizedObjectResult(new
                    {
                        Message = "Not authorized."
                    });

                var result = Sql.Read("SELECT [user_id] FROM Access_Token WHERE [token]=@token AND [expiry]>@date",
                                new Dictionary<string, object>
                                {
                                {"@token", access.access_token },
                                {"@date", Utilz.ToMtn().ToString("s") }
                                });
                if (result.Count == 0)
                    throw new ArgumentException("The Access Token may have expired.");

                userId = result["user_id"][0];
                return null;
            }
            catch (ArgumentException e)
            {
                return this_.BadRequest(new
                {
                    Message = e.Message
                });
            }
            catch (Exception e)
            {
                return this_.StatusCode(500, new
                {
                    Message = $"An internal server error occurred: {e}"
                });
            }
        }
    }
}
