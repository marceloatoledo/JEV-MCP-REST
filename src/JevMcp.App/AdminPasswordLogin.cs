using System.Security.Cryptography;
using System.Text;
using JevMcp.Data;

namespace JevMcp.App;

/// <summary>
/// Configured admin login. Empty username or password never matches, including when
/// the form is also empty.
/// </summary>
internal static class AdminPasswordLogin
{
    public const string AuthenticationType = "AdminPassword";

    public static bool Matches(AccessControlOptions options, string? username, string? password)
    {
        ArgumentNullException.ThrowIfNull(options);

        var expectedUser = options.Username ?? "";
        var expectedPass = options.Password ?? "";
        var presentedUser = (username ?? "").Trim();
        var presentedPass = password ?? "";

        var userOk = expectedUser.Length > 0 &&
            string.Equals(expectedUser, presentedUser, StringComparison.Ordinal);
        var passOk = expectedPass.Length > 0 && FixedEquals(expectedPass, presentedPass);
        return userOk && passOk;
    }

    private static bool FixedEquals(string expected, string presented)
    {
        var left = Encoding.UTF8.GetBytes(expected);
        var right = Encoding.UTF8.GetBytes(presented);
        if (left.Length != right.Length)
        {
            CryptographicOperations.FixedTimeEquals(left, left);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
