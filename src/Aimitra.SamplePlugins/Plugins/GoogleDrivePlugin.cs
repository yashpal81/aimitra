using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.Internal;
using Google.Apis.Core;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2;

namespace Aimitra.SamplePlugins.Plugins
{
    public class CustomMockClock : Google.Apis.Util.IClock
{
    public DateTime Now => DateTime.Now;
    public DateTime UtcNow => DateTime.UtcNow;
}
    public sealed class GoogleDrivePlugin
    {
        private static readonly string[] Scopes = new[]
        {
            DriveService.Scope.DriveReadonly,
            DriveService.Scope.DriveMetadataReadonly
        };

        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _redirectUri;
        private readonly string _tokenStoreDirectory;

        public GoogleDrivePlugin()
        {
            _clientId = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_CLIENT_ID") ?? string.Empty;
            _clientSecret = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_CLIENT_SECRET") ?? string.Empty;
            _redirectUri = Environment.GetEnvironmentVariable("GOOGLE_DRIVE_OAUTH_REDIRECT_URI")?.Trim() ?? "http://localhost:5000/google-drive/callback";
            _tokenStoreDirectory = Path.Combine(AppContext.BaseDirectory, "App_Data", "GoogleDriveTokenStore");
        }

        private void EnsureClientCredentials()
        {
            if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            {
                throw new InvalidOperationException(
                    "Google Drive OAuth client credentials are missing. Set GOOGLE_DRIVE_CLIENT_ID and GOOGLE_DRIVE_CLIENT_SECRET in environment variables.");
            }
        }

        private AuthorizationCodeFlow CreateFlow()
        {
            EnsureClientCredentials();

            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                },
                Scopes = Scopes,
                DataStore = new FileDataStore(_tokenStoreDirectory, true)
            });
        }

        private async Task<UserCredential> GetCredentialAsync(CancellationToken cancellationToken = default)
        {
            var flow = CreateFlow();
            var token = await flow.LoadTokenAsync("google-drive", cancellationToken).ConfigureAwait(false);
            if (token == null)
            {
                throw new InvalidOperationException(
                    "Google Drive has not been authorized yet. Call StartGoogleDriveOAuth to obtain an authorization URL, then complete authorization with CompleteGoogleDriveOAuth.");
            }

            var credential = new UserCredential(flow, "google-drive", token);
            if (credential.Token.IsExpired(new CustomMockClock()))
            {
                if (string.IsNullOrWhiteSpace(credential.Token.RefreshToken))
                {
                    throw new InvalidOperationException("Google Drive refresh token is unavailable. Reauthorize using StartGoogleDriveOAuth.");
                }

                await credential.RefreshTokenAsync(cancellationToken).ConfigureAwait(false);
            }

            return credential;
        }

        [KernelFunction, Description("Returns the current Google Drive OAuth authorization status.")]
        public string GetGoogleDriveOAuthStatus()
        {
            if (string.IsNullOrWhiteSpace(_clientId) || string.IsNullOrWhiteSpace(_clientSecret))
            {
                return "Google Drive OAuth credentials are not configured. Set GOOGLE_DRIVE_CLIENT_ID and GOOGLE_DRIVE_CLIENT_SECRET.";
            }

            if (!Directory.Exists(_tokenStoreDirectory))
            {
                return $"Google Drive is configured, but not yet authorized. Use StartGoogleDriveOAuth to obtain an authorization URL and then call CompleteGoogleDriveOAuth with the code.";
            }

            var tokenFile = Path.Combine(_tokenStoreDirectory, "google-drive");
            return File.Exists(tokenFile)
                ? "Google Drive authorization token is present. The connection is ready to use."
                : "Google Drive is configured, but no authorization token was found. Use StartGoogleDriveOAuth to begin authorization.";
        }

        [KernelFunction, Description("Creates a Google Drive OAuth authorization URL for on-demand authorization.")]
        public string StartGoogleDriveOAuth(string? redirectUri = null)
        {
            string AuthorizationServerUrl = "https://accounts.google.com/o/oauth2/v2/auth";
            EnsureClientCredentials();
            var effectiveRedirectUri = _redirectUri;//string.IsNullOrWhiteSpace(redirectUri) ? _redirectUri : redirectUri.Trim();

            // var authorizationUrl = new AuthorizationCodeRequestUrl(new Uri(GoogleAuthConsts.AuthorizationUrl))
            // {
            //     ClientId = _clientId,
            //     Scope = string.Join(" ", Scopes),
            //     RedirectUri = effectiveRedirectUri,
            //     AccessType = "offline",
            //     IncludeGrantedScopes = true,
            //     Prompt = "consent"
            // };
            var authorizationUrl = new GoogleAuthorizationCodeRequestUrl(new Uri(AuthorizationServerUrl))
            {
                        ClientId = _clientId,
                            Scope = string.Join(" ", Scopes),
                            RedirectUri = effectiveRedirectUri,
                            AccessType = "offline",
                            IncludeGrantedScopes = "true",
                            Prompt = "consent"

                // ClientId = clientId,
                // RedirectUri = redirectUri,
                // Scope = scope,
                // AccessType = "offline" //  Valid on Google subclasses
            };

            return authorizationUrl.Build().ToString();
        }

        [KernelFunction, Description("Exchanges a Google Drive OAuth authorization code for access and refresh tokens.")]
        public async Task<string> CompleteGoogleDriveOAuth(string code, string? redirectUri = null)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return "Authorization code is required to complete Google Drive OAuth.";
            }

            var effectiveRedirectUri = string.IsNullOrWhiteSpace(redirectUri) ? _redirectUri : redirectUri.Trim();
            var flow = CreateFlow();
            var token = await flow.ExchangeCodeForTokenAsync("google-drive", code.Trim(), effectiveRedirectUri, CancellationToken.None).ConfigureAwait(false);
            await flow.DataStore.StoreAsync("google-drive", token).ConfigureAwait(false);

            return $"Google Drive is authorized. Access token expires in {token.ExpiresInSeconds ?? 0} seconds.";
        }

        [KernelFunction, Description("Lists the first set of files from the connected Google Drive account.")]
        public async Task<string> ListGoogleDriveFiles(string query = "trashed=false", int pageSize = 20)
        {
            var credential = await GetCredentialAsync().ConfigureAwait(false);
            using var service = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "Aimitra Google Drive Plugin"
            });

            var request = service.Files.List();
            request.Q = query;
            request.PageSize = Math.Clamp(pageSize, 1, 100);
            request.Fields = "nextPageToken, files(id, name, mimeType, webViewLink, modifiedTime)";

            var response = await request.ExecuteAsync().ConfigureAwait(false);
            if (response.Files == null || response.Files.Count == 0)
            {
                return "No files were found in Google Drive for the current query.";
            }

            var builder = new StringBuilder();
            builder.AppendLine("Google Drive files:");
            foreach (var file in response.Files)
            {
                builder.AppendLine($"- {file.Name} ({file.Id}) [{file.MimeType}] {file.WebViewLink}");
            }

            return builder.ToString().TrimEnd();
        }
    }
}
