
namespace UACloudTwin.Controllers
{
    using Microsoft.AspNetCore.Authentication;
    using Microsoft.AspNetCore.Authentication.Cookies;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using System;
    using System.Collections.Generic;
    using System.Security.Claims;
    using UACloudTwin.Models;

    public class AuthController : Controller
    {
        public IActionResult Index(string returnUrl)
        {
            return View(new StatusModel() { Status = returnUrl });
        }

        public IActionResult Login(string username, string password, string returnUrl)
        {
            if (ModelState.IsValid)
            {
                if (!AuthenticateUser(username, password))
                {
                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                   return View("Error", new StatusModel() { Status = "Access Denied!" });
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim(ClaimTypes.Role, "Administrator"),
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    IssuedUtc = DateTimeOffset.UtcNow
                };

                HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties).GetAwaiter().GetResult();

                return Redirect(returnUrl);
            }

            return View();
        }

        private bool AuthenticateUser(string username, string password)
        {
            if (string.IsNullOrEmpty(username)
            ||  string.IsNullOrEmpty(password)
            ||  string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ADMIN_USERNAME"))
            ||  string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ADMIN_PASSWORD")))
            {
                return false;
            }

            return (username == Environment.GetEnvironmentVariable("ADMIN_USERNAME"))
                && (password == Environment.GetEnvironmentVariable("ADMIN_PASSWORD"));
        }
    }
}
