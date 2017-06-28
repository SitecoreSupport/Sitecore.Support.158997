using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using Sitecore.Data;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Social.Facebook.Connector.Managers;
using Sitecore.Social.Facebook.Connector.Paths;
using Sitecore.Social.Facebook.Exceptions.Analyzers;
using Sitecore.Social.Facebook.Networks.Messages;
using Sitecore.Social.Infrastructure.Exceptions;
using Sitecore.Social.Infrastructure.Utils;
using Sitecore.Social.NetworkProviders;
using Sitecore.Social.NetworkProviders.Args;
using Sitecore.Social.NetworkProviders.Connector.Paths;
using Sitecore.Social.NetworkProviders.Exceptions;
using Sitecore.Social.NetworkProviders.Interfaces;
using Sitecore.Social.NetworkProviders.Messages;
using Sitecore.Social.NetworkProviders.NetworkFields;
using Sitecore.Social.SitecoreAccess;
using Facebook;
using Sitecore.Web;
using Sitecore.Social.Facebook.Networks.Providers;
using Sitecore.Social.Facebook.Networks;
using Newtonsoft.Json.Linq;

namespace Sitecore.Support.Social.Facebook.Networks.Providers
{
    /// <summary>
    /// Represents the network provider for Facebook.
    /// </summary>
    public class FacebookProvider : NetworkProvider, IMessagePosting, IAuth, IGetAccountInfo, IMessageStatistics, IRenewAccount, IMessageActions, IGetApplicationScopedIds
    {
        /// <summary>
        /// The "likes" counter key.
        /// </summary>
        public const string LikesCounterKey = "likes";

        /// <summary>
        /// The "comments" counter key.
        /// </summary>
        public const string CommentsCounterKey = "comments";

        /// <summary>
        /// The expires parameter name
        /// </summary>
        private const string ExpiresParameterName = "expires";

        /// <summary>
        /// The access token parameter name
        /// </summary>
        private const string AccessTokenParameterName = "access_token";

        /// <summary>
        /// Initializes a new instance of the <see cref="FacebookProvider"/> class.
        /// </summary>
        /// <param name="application">The application.</param>
        public FacebookProvider(Sitecore.Social.NetworkProviders.Application application)
          : base(application)
        {
        }

        /// <summary>
        /// Gets the account pages.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <returns></returns>
        public List<FacebookAccount> GetAccountPages(Account account)
        {
            var fbApi = new FacebookAPI(account.AccessTokenSecret);
            var feedpath = string.Format(CultureInfo.CurrentCulture, FacebookPathsFactory.Facebook.API.Accounts, account.AccessToken);
            try
            {
                var responce = fbApi.Get(feedpath);
                var jsonObjects = responce.Dictionary["data"].Array;

                return (from jsonObject in jsonObjects
                        where jsonObject.IsDictionary &&
                          jsonObject.Dictionary.ContainsKey("id") &&
                          jsonObject.Dictionary.ContainsKey("access_token") &&
                          jsonObject.Dictionary.ContainsKey("name") &&
                          jsonObject.Dictionary.ContainsKey("category")
                        select new FacebookAccount
                        {
                            AccessToken = (jsonObject.Dictionary["access_token"].IsString) ? jsonObject.Dictionary["access_token"].String : "",
                            Category = (jsonObject.Dictionary["category"].IsString) ? jsonObject.Dictionary["category"].String : "",
                            Id = (jsonObject.Dictionary["id"].IsString) ? jsonObject.Dictionary["id"].String : "",
                            Name = (jsonObject.Dictionary["name"].IsString) ? jsonObject.Dictionary["name"].String : ""
                        }).ToList();
            }
            catch (Exception e)
            {
                new FacebookExceptionAnalyzer().ThrowByStatusCode(e);
            }

            return null;
        }

        public PostResult PostMessage(Account account, Message message)
        {
            if (!(message is FacebookMessage))
            {
                return null;
            }

            var facebookMessage = message as FacebookMessage;

            var feedpath = string.Format(CultureInfo.CurrentCulture, FacebookPathsFactory.Facebook.API.Feed, account.AccessToken);

            Dictionary<string, string> response = this.FacebookRequest(
              account,
              feedpath,
              facebookMessage,
              (facebookClient, feedPath, inputParams) =>
              {
                  var result = facebookClient.Post(feedPath, this.CreateMessageParameters(inputParams as FacebookMessage)) as JsonObject;

                  if (result != null)
                  {
                      return result.Keys.ToDictionary(key => key, key => result[key].ToString());
                  }

                  return null;
              }) as Dictionary<string, string>;

            PostResult postResult = new PostResult
            {
                MessageId = (response != null && response.ContainsKey("id")) ? response["id"] : null,
                Response = response
            };

            return postResult;
        }

        /// <summary>
        /// Gets the display name of the statistics counter.
        /// </summary>
        /// <param name="statisticsCounterName">Name of the statistics counter.</param>
        /// <returns>The display name</returns>
        public string GetStatisticsCounterDisplayName(string statisticsCounterName)
        {
            if (string.Compare(LikesCounterKey, statisticsCounterName, StringComparison.CurrentCultureIgnoreCase) == 0)
            {
                return Translate.Text(Sitecore.Social.Facebook.Common.Texts.Likes);
            }

            if (string.Compare(CommentsCounterKey, statisticsCounterName, StringComparison.CurrentCultureIgnoreCase) == 0)
            {
                return Translate.Text(Sitecore.Social.Facebook.Common.Texts.Comments);
            }

            return statisticsCounterName;
        }

        /// <summary>
        /// Makes a request for Facebook graph API.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="feedPath">The feed path.</param>
        /// <param name="inputParams">The input parameters.</param>
        /// <param name="action">The action.</param>
        /// <returns>The response.</returns>
        /// <exception cref="AuthException">In a case of authorization error.</exception>
        /// <exception cref="SocialException">In a case of bad request for graph API.</exception>
        private IDictionary<string, object> FacebookRequest(Account account, string feedPath, object inputParams, Func<FacebookClient, string, object, IDictionary<string, object>> action)
        {
            try
            {
                var facebookClient = new FacebookClient(account.AccessTokenSecret);
                return action(facebookClient, feedPath, inputParams);
            }
            catch (FacebookApiLimitException ex)
            {
                throw new AuthException(ex);
            }
            catch (FacebookOAuthException ex)
            {
                throw new AuthException(ex);
            }
            catch (FacebookApiException ex)
            {
                throw new SocialException(ex);
            }
            catch (Exception ex)
            {
                throw new SocialException(ex);
            }
        }

        /// <summary>
        /// Makes a request for Facebook graph API.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="feedPath">The feed path.</param>
        /// <param name="inputParams">The input parameters.</param>
        /// <param name="action">The action.</param>
        /// <returns>The response.</returns>
        /// <exception cref="AuthException">In a case of authorization error.</exception>
        /// <exception cref="SocialException">In a case of bad request for graph API.</exception>
        private object FacebookRequest(
          Account account, string feedPath, object inputParams, Func<FacebookClient, string, object, object> action)
        {
            try
            {
                var facebookClient = new FacebookClient(account.AccessTokenSecret);
                return action(facebookClient, feedPath, inputParams);
            }
            catch (FacebookApiLimitException ex)
            {
                throw new AuthException(ex);
            }
            catch (FacebookOAuthException ex)
            {
                throw new AuthException(ex);
            }
            catch (FacebookApiException ex)
            {
                throw new SocialException(ex);
            }
            catch (Exception ex)
            {
                throw new SocialException(ex);
            }
        }

        /// <summary>
        /// Gets the account data.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="access">The access.</param>
        /// <param name="fields">The fields.</param>
        /// <returns></returns>
        private IDictionary<string, object> GetAccountData(Account account, string access, IEnumerable<string> fields)
        {
            return this.FacebookRequest(
              account,
              access,
              null,
              (facebookClient, feedPath, inputParams) => facebookClient.Get(feedPath, new Dictionary<string, object>() { { "fields", string.Join(",", fields) } }) as IDictionary<string, object>);
        }

        /// <summary>
        /// Gets the account data.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="access">The access.</param>
        /// <param name="field">The field.</param>
        /// <returns></returns>
        private IDictionary<string, object> GetAccountData(Account account, string access, string field)
        {
            return this.GetAccountData(
              account,
              access,
              new List<string>
              {
          field
              });
        }

        /// <summary>
        /// Gets the response dictionary.
        /// </summary>
        /// <param name="dictionary">The dictionary.</param>
        /// <returns></returns>
        protected Dictionary<string, string> GetResponseDictionary(Dictionary<string, JSONObject> dictionary)
        {
            return dictionary.Keys.ToDictionary(key => key, key => dictionary[key].ToDisplayableString());
        }

        /// <summary>
        /// Creates the parameters.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns></returns>
        protected Dictionary<string, object> CreateMessageParameters(FacebookMessage message)
        {
            var dict = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(message.Text) || !string.IsNullOrEmpty(message.Link))
            {
                dict.Add("message", message.Text);


                if (!string.IsNullOrEmpty(message.Link))
                {
                    dict.Add("link", message.Link);

                    if (!string.IsNullOrEmpty(message.Name))
                    {
                        dict.Add("name", message.Name);
                    }

                    if (!string.IsNullOrEmpty(message.Link))
                    {

                        if (!string.IsNullOrEmpty(message.Picture))
                        {
                            dict.Add("picture", message.Picture);
                        }

                    }

                    if (!string.IsNullOrEmpty(message.Description))
                    {
                        dict.Add("description", message.Description);
                    }
                }

            }

            return dict;
        }

        public void AuthGetCode(AuthArgs args)
        {
            var redirectUrl = WebUtil.GetFullUrl(Paths.SocialLoginHandlerPath + "?type=access");

            var permissionsString = (string)null;
            if (args.Permissions != null && args.Permissions.Count > 0)
            {
                var permissions = args.Permissions.Select(p => p.Key).ToList();
                var permStr = new StringBuilder();
                permStr.Append(permissions[0]);
                for (int i = 1; i < permissions.Count; i++)
                {
                    permStr.Append(',');
                    permStr.Append(permissions[i]);
                }
                permissionsString = permStr.ToString();
            }


            var url =
              string.Format(CultureInfo.CurrentCulture,
                FacebookPathsFactory.Facebook.API.Oauth + "?client_id={0}&redirect_uri={1}&scope={2}&type=web_server&state={3}",
                args.Application.ApplicationKey,
                redirectUrl,
                permissionsString,
                args.StateKey);
            RedirectUtil.Redirect(url);
        }

        public void AuthGetAccessToken(AuthArgs args)
        {
            var request = System.Web.HttpContext.Current.Request;

            var cancel = request.QueryString.Get("error");
            if (string.IsNullOrEmpty(cancel))
            {
                var code = request.QueryString.Get("code");

                if (!string.IsNullOrEmpty(code))
                {
                    string redirectUrl = WebUtil.GetFullUrl(Paths.SocialLoginHandlerPath + "?type=access");

                    var url =
                      string.Format(CultureInfo.CurrentCulture,
                        FacebookPathsFactory.Facebook.API.AccessToken + "?client_id={0}&redirect_uri={1}&client_secret={2}&code={3}",
                        args.Application.ApplicationKey,
                        redirectUrl,
                        args.Application.ApplicationSecret,
                        code);

                    var myRequest = new WebRequestManager(url);

                    var response = myRequest.GetResponse();
                    if (response != null)
                    {
                        var queryString = WebUtil.ParseUrlParameters(response);
                        if (!queryString.AllKeys.Contains(AccessTokenParameterName))
                        {
                            throw new FacebookOAuthException("Access token was not present in the response.");
                        }

                        var accessToken = queryString[AccessTokenParameterName];

                        DateTime? accessTokenSecretIssueDate = null, accessTokenSecretExpirationDate = null;

                        if (queryString.AllKeys.Contains(ExpiresParameterName))
                        {
                            accessTokenSecretIssueDate = DateTime.UtcNow;
                            accessTokenSecretExpirationDate = accessTokenSecretIssueDate.Value.AddSeconds(int.Parse(queryString[ExpiresParameterName]));
                        }

                        var authCompletedArgs = new AuthCompletedArgs
                        {
                            Application = args.Application,
                            AccessTokenSecret = accessToken,
                            CallbackPage = args.CallbackUrl,
                            ExternalData = args.ExternalData,
                            AttachAccountToLoggedInUser = args.AttachAccountToLoggedInUser,
                            IsAsyncProfileUpdate = args.IsAsyncProfileUpdate,
                            AccessTokenSecretIssueDate = accessTokenSecretIssueDate,
                            AccessTokenSecretExpirationDate = accessTokenSecretExpirationDate
                        };

                        if (!string.IsNullOrEmpty(args.CallbackType))
                        {
                            this.InvokeAuthCompleted(args.CallbackType, authCompletedArgs);
                        }
                    }
                    else
                    {
                        new FacebookExceptionAnalyzer().Analyze(myRequest.ErrorText);
                    }
                }
            }
        }

        public string GetAccountId(Account account)
        {
            var accountData = this.GetAccountData(account, FacebookPathsFactory.Facebook.API.User, "id");
            return accountData != null ? accountData["id"] as string : null;
        }

        public AccountBasicData GetAccountBasicData(Account account)
        {
            Assert.IsNotNull(account, "Account parameter is null");

            var accountData = this.GetAccountData(account, FacebookPathsFactory.Facebook.API.User, new List<string>() { "first_name", "last_name", "id", "email" });

            if (accountData != null)
            {
                var fullName = accountData["first_name"] + " " + accountData["last_name"];
                return new AccountBasicData
                {
                    Account = account,
                    Id = accountData["id"] as string,
                    Email = accountData.ContainsKey("email") ? accountData["email"] as string : null,
                    FullName = fullName
                };
            }

            return null;
        }

        public string GetDisplayName(Account account)
        {
            var accountData = this.GetAccountData(account, FacebookPathsFactory.Facebook.API.User, new List<string>() { "first_name", "last_name" });

            // FIX: accountData["name"] contains an extra space between first and last names
            return accountData != null ? string.Format("{0} {1}", accountData["first_name"], accountData["last_name"]) : null;
        }

        /// <summary>
        /// The method gets the profile fields from social netwok, matches them with accepted field list
        /// </summary>
        /// <param name="account">The network account</param>
        /// <param name="acceptedFields">The list of accepted fields</param>
        /// <returns>The collection with a sitecore (not original) field name as a key and value</returns>
        public IEnumerable<Field> GetAccountInfo(Account account, IEnumerable<FieldInfo> acceptedFields)
        {
            Assert.IsNotNull(acceptedFields, "AcceptedFields collection must be filled");

            var fieldsGroupedByAccess = acceptedFields.Where(field => field["access"] != null)
              .GroupBy(field => field["access"])
              .Select(fg => new { Access = fg.Key, Fields = fg.ToList() });

            foreach (var fieldGroup in fieldsGroupedByAccess)
            {
                var accountData = this.GetAccountData(account, "/" + FacebookPathsFactory.Facebook.API.ApiVersion + fieldGroup.Access, fieldGroup.Fields.Select(x => x.OriginalKey));
                if (accountData != null)
                {
                    foreach (var field in fieldGroup.Fields)
                    {
                        // when original key is not set in profile mapping config
                        // then put all of the responce data as displayable string
                        if (!string.IsNullOrEmpty(field.OriginalKey))
                        {
                            if (accountData.ContainsKey(field.OriginalKey))
                            {
                                var fieldValue = accountData[field.OriginalKey].ToString();

                                yield return new Field
                                {
                                    Name = this.GetFieldSitecoreKey(field),
                                    Value = fieldValue
                                };
                            }
                        }
                        else
                        {
                            var resultAsDisplayableString = (accountData.ContainsKey("data")) ? accountData["data"].ToString() : accountData.ToString();
                            if (resultAsDisplayableString != "[]")
                            {
                                yield return new Field
                                {
                                    Name = this.GetFieldSitecoreKey(field),
                                    Value = resultAsDisplayableString
                                };
                            }

                        }
                    }
                }
            }
        }

        public List<string> StatisticNames
        {
            get
            {
                return new List<string> { LikesCounterKey, CommentsCounterKey };
            }
        }

        public Dictionary<string, double> GetMessageStatistics(Account account, string messageId)
        {
            //string new_post = "v2.8/{0}";

            var feedpath = string.Format(CultureInfo.CurrentCulture, FacebookPathsFactory.Facebook.API.Post, messageId);

            string likes_feedpath = feedpath + "/likes?summary=true";
            string comments_feedpath = feedpath + "/comments?summary=true";


            var result = new Dictionary<string, object> { { LikesCounterKey, 0 }, { CommentsCounterKey, 0 } };


            Func<FacebookClient, string, object, Dictionary<string, object>> func =
            (facebookClient, request, inputParams) =>
            {
                var fbResult = facebookClient.Get(request) as JsonObject;

                var json = fbResult.ToString();

                JObject o = JObject.Parse(json);

                double count = o["summary"]["total_count"].Value<double>();

                if (request.Contains("likes"))
                {
                    result[LikesCounterKey] = count;
                }
                else
                {
                    result[CommentsCounterKey] = count;
                }

                return null;
            };


            this.FacebookRequest(account, likes_feedpath, messageId, func);

            this.FacebookRequest(account, comments_feedpath, messageId, func);

            return result.ToDictionary(pair => pair.Key, pair => System.Convert.ToDouble(pair.Value));
        }

        /// <summary>
        /// Renews the account.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="redirectUrl">The redirect URL.</param>
        public void RenewAccount(ID accountItemId, Account account, string redirectUrl)
        {
            var url = FacebookPathsFactory.Facebook.API.AccessToken +
              "?client_id=" + HttpUtility.UrlEncode(account.Application.ApplicationKey) +
              "&client_secret=" + HttpUtility.UrlEncode(account.Application.ApplicationSecret) +
              "&grant_type=fb_exchange_token&fb_exchange_token=" + HttpUtility.UrlEncode(account.AccessTokenSecret) +
              "&redirect_uri=" + HttpUtility.UrlEncode(redirectUrl);

            string accessToken;
            var accessTokenRequest = WebRequest.Create(url);
            var response = (HttpWebResponse)accessTokenRequest.GetResponse();

            var responseEncoding = Encoding.GetEncoding(response.CharacterSet);

            using (var responseReader = new StreamReader(response.GetResponseStream(), responseEncoding))
            {
                accessToken = HttpUtility.ParseQueryString(responseReader.ReadToEnd()).Get("access_token");
            }

            var accountItem = CurrentDatabase.Database.GetItem(accountItemId);
            if ((accountItem != null) && !string.IsNullOrEmpty(accessToken))
            {
                accountItem.Editing.BeginEdit();
                accountItem.Fields["AccessTokenSecret"].Value = accessToken;
                accountItem.Fields["LastRenewalDate"].Value = DateTime.UtcNow.ToIso();
                accountItem.Editing.EndEdit();
            }
        }

        /// <summary>
        /// Gets the message comments.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="messageId">The message id.</param>
        /// <returns>
        /// The list of the message comments.
        /// </returns>
        public IEnumerable<MessageComment> GetMessageComments(Account account, string messageId)
        {
            var feedpath = string.Format(CultureInfo.CurrentCulture, FacebookPathsFactory.Facebook.API.Comments, messageId);

            var response = this.FacebookRequest(
              account,
              feedpath,
              messageId,
              (facebookClient, feedPath, inputParams) =>
              {
                  var fbResult = facebookClient.Get(feedPath) as JsonObject;

                  if (fbResult != null)
                  {
                      if (fbResult.ContainsKey("data") && fbResult["data"] is JsonArray)
                      {
                          return (fbResult["data"] as JsonArray).Select(comment => this.GetMessageComment(comment as JsonObject));
                      }
                  }

                  return null;
              });

            return response as IEnumerable<MessageComment>;
        }

        /// <summary>
        /// Gets the application scoped ids.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <returns>The enumerable of application scoped ids.</returns>
        public IEnumerable<string> GetApplicationScopedIds(Account account)
        {
            var jsonResult = this.FacebookRequest(
              account,
              FacebookPathsFactory.Facebook.API.IdsForBusiness,
              null,
              (facebookClient, feedPath, inputParams) => facebookClient.Get(feedPath)) as JsonObject;

            if (jsonResult == null || !jsonResult.ContainsKey("data"))
            {
                return null;
            }

            var applicationScopedIds = new List<string>();
            foreach (JsonObject arrayItem in jsonResult["data"] as JsonArray)
            {
                applicationScopedIds.Add(arrayItem["id"].ToString());
            }

            return applicationScopedIds;
        }

        /// <summary>
        /// Gets the message comment.
        /// </summary>
        /// <param name="commentJsonObject">The comment json object.</param>
        /// <returns>The message comment.</returns>
        private MessageComment GetMessageComment(JsonObject commentJsonObject)
        {
            var result = new MessageComment();
            if (commentJsonObject != null)
            {
                if (commentJsonObject.ContainsKey("id"))
                {
                    result.NetworkCommentId = commentJsonObject["id"].ToString();
                }

                result.Text = commentJsonObject.ContainsKey("message") ? commentJsonObject["message"].ToString() : null;

                // try to convert ISO 8601 to DateTime
                // https://developers.facebook.com/docs/graph-api/using-graph-api/v2.1
                result.CreatedDate = commentJsonObject.ContainsKey("created_time")
                  ? DateTime.Parse(commentJsonObject["created_time"].ToString(), null, DateTimeStyles.RoundtripKind).ToUtc()
                  : DateTime.MinValue;

                if (commentJsonObject.ContainsKey("from") && commentJsonObject["from"] is JsonObject)
                {
                    var fromJsonObject = commentJsonObject["from"] as JsonObject;

                    result.AuthorName = fromJsonObject.ContainsKey("name") ? fromJsonObject["name"].ToString() : null;
                }
            }

            return result;
        }
    }
}