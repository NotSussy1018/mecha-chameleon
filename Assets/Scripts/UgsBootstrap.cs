using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Core.Environments;
using UnityEngine;

namespace MechaChameleon
{
    public static class UgsBootstrap
    {
        public const int MinUsernameLength = 3;
        public const int MaxUsernameLength = 20;
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 30;

        static Task initializationTask;

        public static bool IsCloudProjectLinked => !string.IsNullOrWhiteSpace(Application.cloudProjectId);
        public static bool IsSignedIn =>
            UnityServices.State == ServicesInitializationState.Initialized &&
            AuthenticationService.Instance.IsSignedIn;
        public static string Username => IsSignedIn
            ? AuthenticationService.Instance.PlayerInfo?.Username ?? ""
            : "";
        public static string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            initializationTask = null;
        }

        public static async Task EnsureReadyAsync()
        {
            await EnsureInitializedAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                throw new InvalidOperationException("Sign in before using online rooms.");
        }

        public static async Task<bool> TryRestoreSessionAsync()
        {
            await EnsureInitializedAsync();
            var authentication = AuthenticationService.Instance;
            if (authentication.IsSignedIn) return true;
            if (!authentication.SessionTokenExists) return false;

            try
            {
                // This SDK entry point consumes the cached session token first; CreateAccount=false prevents fallback.
                await authentication.SignInAnonymouslyAsync(new SignInOptions { CreateAccount = false });
                GameDiagnostics.Info("auth", "session_restored", "provider=username_password");
                return true;
            }
            catch (RequestFailedException exception) when (
                exception.ErrorCode == AuthenticationErrorCodes.InvalidSessionToken ||
                exception.ErrorCode == AuthenticationErrorCodes.ClientNoActiveSession ||
                exception.ErrorCode == CommonErrorCodes.InvalidToken ||
                exception.ErrorCode == CommonErrorCodes.TokenExpired)
            {
                authentication.SignOut(true);
                GameDiagnostics.Warning("auth", "session_restore_rejected",
                    $"errorCode={exception.ErrorCode}");
                return false;
            }
        }

        public static async Task SignInAsync(string username, string password)
        {
            var validation = ValidateCredentials(username, password);
            if (validation.Length > 0) throw new ArgumentException(validation);

            await EnsureInitializedAsync();
            await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username.Trim(), password);
            GameDiagnostics.Info("auth", "sign_in_succeeded", "provider=username_password");
        }

        public static async Task SignUpAsync(string username, string password)
        {
            var validation = ValidateCredentials(username, password);
            if (validation.Length > 0) throw new ArgumentException(validation);

            await EnsureInitializedAsync();
            await AuthenticationService.Instance.SignUpWithUsernamePasswordAsync(username.Trim(), password);
            GameDiagnostics.Info("auth", "sign_up_succeeded", "provider=username_password");
        }

        public static void SignOut()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized) return;
            AuthenticationService.Instance.SignOut(true);
            GameDiagnostics.Info("auth", "signed_out", "credentialsCleared=true");
        }

        public static string ValidateCredentials(string username, string password)
        {
            username = username?.Trim() ?? "";
            password ??= "";

            if (username.Length < MinUsernameLength || username.Length > MaxUsernameLength)
                return $"Username must be {MinUsernameLength}-{MaxUsernameLength} characters.";

            foreach (var character in username)
            {
                if (character <= 127 && (char.IsLetterOrDigit(character) || character is '.' or '-' or '@' or '_'))
                    continue;
                return "Username can use letters, numbers, dot, dash, @, and underscore.";
            }

            if (password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
                return $"Password must be {MinPasswordLength}-{MaxPasswordLength} characters.";

            var hasUpper = false;
            var hasLower = false;
            var hasNumber = false;
            var hasSpecial = false;
            foreach (var character in password)
            {
                hasUpper |= char.IsUpper(character);
                hasLower |= char.IsLower(character);
                hasNumber |= char.IsDigit(character);
                hasSpecial |= !char.IsLetterOrDigit(character);
            }

            return hasUpper && hasLower && hasNumber && hasSpecial
                ? ""
                : "Password needs upper, lower, number, and special characters.";
        }

        public static string GetAuthenticationError(Exception exception, bool signingUp)
        {
            if (exception is ArgumentException) return exception.Message;
            if (exception is InvalidOperationException) return exception.Message;
            if (exception is not RequestFailedException request)
                return signingUp ? "Could not create account." : "Could not sign in.";

            if (request.ErrorCode is CommonErrorCodes.TransportError or CommonErrorCodes.Timeout or
                CommonErrorCodes.ServiceUnavailable)
                return "Could not reach Unity services. Check your connection.";
            if (request.ErrorCode == CommonErrorCodes.TooManyRequests)
                return "Too many attempts. Please wait and try again.";
            if (request.ErrorCode == CommonErrorCodes.Conflict && signingUp)
                return "That username is already in use.";
            if (request.ErrorCode == AuthenticationErrorCodes.BannedUser)
                return "This account cannot sign in.";
            if (request.ErrorCode == AuthenticationErrorCodes.InvalidParameters)
                return "Username or password format is invalid.";

            return signingUp
                ? "Could not create account. Try another username."
                : "Username or password is incorrect.";
        }

        static async Task EnsureInitializedAsync()
        {
            if (!IsCloudProjectLinked)
                throw new InvalidOperationException(
                    "Unity Cloud Project is not linked. Link it in Project Settings > Services, then retry staging.");

            initializationTask ??= InitializeAsync();
            try
            {
                await initializationTask;
            }
            catch
            {
                initializationTask = null;
                throw;
            }
        }

        static async Task InitializeAsync()
        {
            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                var options = new InitializationOptions()
                    .SetEnvironmentName(DeploymentEnvironmentSettings.UgsEnvironmentName);

#if UNITY_EDITOR
                var profileHash = Hash128.Compute(Application.dataPath).ToString();
                options.SetProfile($"editor-{profileHash.Substring(0, 16)}");
#endif
                await UnityServices.InitializeAsync(options);
            }
        }
    }
}
