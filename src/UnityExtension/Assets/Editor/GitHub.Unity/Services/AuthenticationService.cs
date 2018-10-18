﻿using System;
using System.Text;
using System.Threading;
using GitHub.Logging;

namespace GitHub.Unity
{
    class AuthenticationService: IDisposable
    {
        private readonly ITaskManager taskManager;
        private static readonly ILogging logger = LogHelper.GetLogger<AuthenticationService>();

        private readonly IApiClient client;

        private LoginResult loginResultData;
        private IOAuthCallbackListener oauthCallbackListener;
        private CancellationTokenSource oauthCallbackCancellationToken;
        private string oauthCallbackState;

        public AuthenticationService(UriString host, IKeychain keychain,
            IProcessManager processManager, ITaskManager taskManager,
            IEnvironment environment
        )
        {
            this.taskManager = taskManager;
            client = host == null
                ? new ApiClient(keychain, processManager, taskManager, environment)
                : new ApiClient(host, keychain, processManager, taskManager, environment);
        }

        public HostAddress HostAddress { get { return client.HostAddress; } }

        public void Login(string username, string password, Action<string> twofaRequired, Action<bool, string> authResult)
        {
            loginResultData = null;
            client.Login(username, password, r =>
            {
                loginResultData = r;
                twofaRequired(r.Message);
            }, authResult);
        }

        public void LoginWithToken(string token, Action<bool> authResult)
        {
            client.LoginWithToken(token, authResult);
        }

        public void LoginWith2fa(string code)
        {
            if (loginResultData == null)
                throw new InvalidOperationException("Call Login() first");
            client.ContinueLogin(loginResultData, code);
        }

        public void GetServerMeta(Action<GitHubHostMeta> serverMeta, Action<string> error)
        {
            loginResultData = null;
            client.GetEnterpriseServerMeta(data =>
            {
                serverMeta(data);
            }, exception => {
                error(exception.Message);
            });
        }

        public Uri StartOAuthListener(Action onSuccess, Action<string> onError)
        {
            if (oauthCallbackListener == null)
            {
                logger.Trace("Start OAuthCallbackListener");
                oauthCallbackListener = new OAuthCallbackListener();
                oauthCallbackCancellationToken = new CancellationTokenSource();

                oauthCallbackState = Guid.NewGuid().ToString();
                oauthCallbackListener.Listen(
                    oauthCallbackState,
                    oauthCallbackCancellationToken.Token,
                    code => {
                        logger.Trace("OAuthCallbackListener Response: {0}", code);

                        client.CreateOAuthToken(code, (b, s) => {
                            if (b)
                            {
                                onSuccess();
                            }
                            else
                            {
                                onError(s);
                            }
                        });
                    });
            }

            return GetLoginUrl(oauthCallbackState);
        }

        public void StopOAuthListener()
        {
            if (oauthCallbackCancellationToken != null)
            {
                oauthCallbackCancellationToken.Cancel();
            }

            oauthCallbackListener = null;
            oauthCallbackCancellationToken = null;
        }

        private Uri GetLoginUrl(string state)
        {
            var query = new StringBuilder();

            query.Append("client_id=");
            query.Append(Uri.EscapeDataString(ApplicationInfo.ClientId));
            query.Append("&redirect_uri=");
            query.Append(Uri.EscapeDataString("http://localhost:42424/callback"));
            query.Append("&scope=");
            query.Append(Uri.EscapeDataString("user,repo"));
            query.Append("&state=");
            query.Append(Uri.EscapeDataString(state));

            var uri = new Uri(HostAddress.WebUri, "login/oauth/authorize");
            var uriBuilder = new UriBuilder(uri)
            {
                Query = query.ToString()
            };
            return uriBuilder.Uri;
        }

        public void Dispose()
        {
            if (oauthCallbackCancellationToken != null)
                oauthCallbackCancellationToken.Dispose();
        }
    }
}
