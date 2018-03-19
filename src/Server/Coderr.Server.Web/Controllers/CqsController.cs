﻿using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Coderr.Server.Api.Core.Applications.Commands;
using Coderr.Server.Api.Core.Applications.Queries;
using Coderr.Server.Infrastructure.Messaging;
using Coderr.Server.Infrastructure.Security;
using Coderr.Server.Web2.Boot.Cqs;
using Coderr.Server.Web2.Infrastrucutre.Results;
using Coderr.Server.Web2.Models.Users;
using DotNetCqs;
using Griffin;
using Griffin.Net.Protocols.Http;
using log4net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace Coderr.Server.Web2.Controllers
{
    [Authorize]
    public class CqsController : Controller
    {
        private static readonly CqsObjectMapper _cqsObjectMapper = new CqsObjectMapper();
        private static readonly MessagingSerializer _serializer = new MessagingSerializer();
        private static readonly MethodInfo _queryMethod;
        private static readonly MethodInfo _sendMethod;
        private readonly ILog _logger = LogManager.GetLogger(typeof(CqsController));
        private readonly IMessageBus _messageBus;
        private readonly IQueryBus _queryBus;

        static CqsController()
        {
            if (_cqsObjectMapper.IsEmpty)
                _cqsObjectMapper.ScanAssembly(typeof(CreateApplication).Assembly);

            _queryMethod = typeof(IQueryBus)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .First(x => x.Name == "QueryAsync" && x.GetParameters().Length == 2);

            _sendMethod = typeof(IMessageBus).GetMethod("SendAsync", new[] { typeof(ClaimsPrincipal), typeof(object) });
        }

        public CqsController(IMessageBus messageBus, IQueryBus queryBus)
        {
            _messageBus = messageBus;
            _queryBus = queryBus;
        }

        [HttpPost("api/authenticate")]
        [HttpPost("authenticate")]
        public async Task<IActionResult> Authenticate()
        {
            if (!User.Identity.IsAuthenticated)
                return Unauthorized();

            var q = new GetApplicationList { AccountId = User.GetAccountId() };
            var result = await _queryBus.QueryAsync(User, q);

            var dto = new AuthenticatedUser
            {
                AccountId = User.GetAccountId(),
                UserName = User.Identity.Name,
                Applications = result
            };
            return Json(dto);
        }

        [HttpGet]
        [HttpPost]
        [Route("api/cqs")]
        [Route("cqs")]
        public async Task<IActionResult> Cqs()
        {
            var headerValue = Request.Headers["X-Cqs-Object-Type"];
            var dotNetType = headerValue.Count > 0
                ? headerValue[0]
                : null;
            headerValue = Request.Headers["X-Cqs-Name"];
            var cqsName = headerValue.Count > 0
                ? headerValue[0]
                : null;

            string json;
            using (var reader
                = new StreamReader(Request.Body, Encoding.UTF8, true, 8192, true))
            {
                json = reader.ReadToEnd();
            }

            object cqsObject, cqsReplyObject = null;
            if (!string.IsNullOrEmpty(dotNetType))
            {
                cqsObject = _cqsObjectMapper.Deserialize(dotNetType, json);
                if (cqsObject == null)
                {
                    _logger.Error($"Could not deserialize[{dotNetType}]: {json}");
                    return BadRequest(new ErrorMessage($"Unknown type: {dotNetType}"));
                }
            }
            else if (!string.IsNullOrEmpty(cqsName))
            {
                cqsObject = _cqsObjectMapper.Deserialize(cqsName, json);
                if (cqsObject == null)
                {
                    _logger.Error($"Could not deserialize[{cqsName}]: {json}");
                    return BadRequest(new ErrorMessage($"Unknown type: {cqsName}"));
                }
            }
            else
            {
                _logger.Error($"Could not deserialize[{cqsName}]: {json}");
                return BadRequest(new ErrorMessage(
                    "Expected a class name in the header 'X-Cqs-Name' or a .NET type name in the header 'X-Cqs-Object-Type'."));
            }

            if (User.Identity.AuthenticationType != "ApiKey")
            {
                var prop = cqsObject.GetType().GetProperty("CreatedById");
                if (prop != null && prop.CanWrite)
                    prop.SetValue(cqsObject, User.GetAccountId());
                prop = cqsObject.GetType().GetProperty("AccountId");
                if (prop != null && prop.CanWrite)
                    prop.SetValue(cqsObject, User.GetAccountId());
                prop = cqsObject.GetType().GetProperty("UserId");
                if (prop != null && prop.CanWrite)
                    prop.SetValue(cqsObject, User.GetAccountId());
            }

            RestrictOnApplicationId(cqsObject);

            Exception ex = null;
            try
            {
                _logger.Debug("Invoking " + cqsObject.GetType().Name + " " + json);
                if (IsQuery(cqsObject))
                    cqsReplyObject = await InvokeQuery(cqsObject);
                else
                    await InvokeMessage(cqsObject);

                if (cqsReplyObject != null)
                    RestrictOnApplicationId(cqsReplyObject);

                await HandleSecurityPrincipalUpdates();
            }
            catch (AggregateException e1)
            {
                _logger.Error("Failed to process '" + json + "'.", e1);
                ex = e1.InnerException;
            }
            catch (Exception e1)
            {
                _logger.Error("Failed to process2 '" + json + "'.", e1);
                ex = e1;
            }

            if (ex is InvalidCredentialException)
            {
                _logger.Error("Auth error for " + json, ex);
                var authEx = (InvalidCredentialException)ex;
                return BadRequest(new ErrorMessage(FirstLine(ex.Message)));
            }
            if (ex != null)
            {
                _logger.Error("Failed to process result for " + json, ex);
                return BadRequest(new ErrorMessage(FirstLine(ex.Message)));
            }

            var result = new ContentResult {ContentType = "application/json"};

            // for instance commands do not have a return value.
            if (cqsReplyObject != null)
            {
                Response.Headers.Add("X-Cqs-Object-Type", cqsReplyObject.GetType().GetSimpleAssemblyQualifiedName());
                Response.Headers.Add("X-Cqs-Name", cqsReplyObject.GetType().Name);
                if (cqsReplyObject is Exception)
                    result.StatusCode = 500;

                json = _serializer.Serialize(cqsReplyObject, out var cnt);
                _logger.Debug("Reply to " + cqsObject.GetType().Name + ": " + json);
                result.Content = json;
            }
            else
            {
                _logger.Debug("Reply to " + cqsObject.GetType().Name + ": [empty response]");
                result.StatusCode = (int)HttpStatusCode.NoContent;
            }

            return result;
        }

        public static bool IsQuery(object cqsObject)
        {
            var baseType = cqsObject.GetType().BaseType;
            while (baseType != null)
            {
                if (baseType.FullName.StartsWith("DotNetCqs.Query"))
                    return true;
                baseType = baseType.BaseType;
            }
            return false;
        }

        private string FirstLine(string msg)
        {
            var pos = msg.IndexOfAny(new[] { '\r', '\n' });
            return pos == -1 ? msg : msg.Substring(0, pos);
        }

        private async Task HandleSecurityPrincipalUpdates()
        {
            var gotUpdate = User.Identities.First().TryRemoveClaim(CoderrClaims.UpdateIdentity);

            //to be sure that there are no other points in the flow that added the same claim
            while (User.Identities.First().TryRemoveClaim(CoderrClaims.UpdateIdentity))
            {
            }

            if (gotUpdate)
            {
                var usr = User;
                SignIn(usr, CookieAuthenticationDefaults.AuthenticationScheme);
            }
        }

        private async Task InvokeMessage(object dto)
        {
            var type = dto.GetType();
            try
            {
                var task = (Task)_sendMethod.Invoke(_messageBus, new[] { User, dto });
                await task;
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private async Task<object> InvokeQuery(object dto)
        {
            var type = dto.GetType();
            var replyType = type.BaseType.GetGenericArguments()[0];
            var method = _queryMethod.MakeGenericMethod(replyType);
            try
            {
                var result = method.Invoke(_queryBus, new[] { User, dto });
                var task = (Task)result;
                await task;
                return ((dynamic)task).Result;
            }
            catch (TargetInvocationException exception)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        private void RestrictOnApplicationId(object cqsObject)
        {
            if (User.Identity.AuthenticationType != "ApiKey")
                return;
            if (User.IsInRole(CoderrClaims.RoleSysAdmin))
                return;

            var prop = cqsObject.GetType().GetProperty("ApplicationId");
            if (prop == null || !prop.CanRead)
                return;

            var value = (int)prop.GetValue(cqsObject);
            if (!User.IsApplicationMember(value))
            {
                _logger.Warn("Tried to access an application without privileges. accountId: " + User.Identity.Name +
                             ", appId: " + value);
                throw new HttpException(403, "The given application key is not allowed for application " + value);
            }
        }
    }
}