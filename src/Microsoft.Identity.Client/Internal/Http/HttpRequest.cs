﻿//----------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Microsoft.Identity.Client.Internal.Http
{
    internal class HttpRequest
    {
        private HttpRequest()
        {
        }

        public static async Task<HttpResponse> SendPost(Uri endpoint, Dictionary<string, string> headers,
            Dictionary<string, string> bodyParameters, RequestContext requestContext)
        {
            return
                await
                    ExecuteWithRetry(endpoint, headers, bodyParameters, HttpMethod.Post, requestContext)
                        .ConfigureAwait(false);
        }

        public static async Task<HttpResponse> SendGet(Uri endpoint, Dictionary<string, string> headers,
            RequestContext requestContext)
        {
            return await ExecuteWithRetry(endpoint, headers, null, HttpMethod.Get, requestContext).ConfigureAwait(false);
        }

        private static HttpRequestMessage CreateRequestMessage(Uri endpoint, Dictionary<string, string> headers)
        {
            HttpRequestMessage requestMessage = new HttpRequestMessage { RequestUri = endpoint };
            requestMessage.Headers.Accept.Clear();
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> kvp in headers)
                {
                    requestMessage.Headers.Add(kvp.Key, kvp.Value);
                }
            }

            return requestMessage;
        }

        private static async Task<HttpResponse> ExecuteWithRetry(Uri endpoint, Dictionary<string, string> headers,
            Dictionary<string, string> bodyParameters, HttpMethod method,
            RequestContext requestContext, bool retry = true)
        {
            Exception toThrow = null;
            bool isRetryable = false;
            HttpResponse response = null;
            try
            {
                response = await Execute(endpoint, headers, bodyParameters, method);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return response;
                }

                requestContext.Logger.Info(
                     string.Format(CultureInfo.InvariantCulture,
                         "Response status code does not indicate success: {0} ({1}).",
                         (int)response.StatusCode, response.StatusCode));

                if ((int)response.StatusCode >= 500 && (int)response.StatusCode < 600)
                {
                    isRetryable = true;
                }
            }
            catch (TaskCanceledException exception)
            {
                requestContext.Logger.Error(exception);
                isRetryable = true;
                toThrow = exception;
            }

            if (isRetryable)
            {
                if (retry)
                {
                    requestContext.Logger.Info("Retrying one more time..");
                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    return await ExecuteWithRetry(endpoint, headers, bodyParameters, method, requestContext, false);
                }

                requestContext.Logger.Info("Request retry failed.");
                if (toThrow != null)
                {
                    throw new MsalServiceException(MsalServiceException.RequestTimeout, "Request to the endpoint timed out.", toThrow);
                }

                throw new MsalServiceException(MsalServiceException.ServiceNotAvailable,
                    "Service is unavailable to process the request", (int) response.StatusCode);
            }

            return response;
        }

        private static async Task<HttpResponse> Execute(Uri endpoint, Dictionary<string, string> headers,
            Dictionary<string, string> bodyParameters, HttpMethod method)
        {
            HttpClient client = HttpClientFactory.GetHttpClient();
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using (HttpRequestMessage requestMessage = CreateRequestMessage(endpoint, headers))
            {
                requestMessage.Method = method;
                if (bodyParameters != null)
                {
                    requestMessage.Content = new FormUrlEncodedContent(bodyParameters);
                }

                using (HttpResponseMessage responseMessage =
                    await client.SendAsync(requestMessage).ConfigureAwait(false))
                {
                    HttpResponse returnValue = await CreateResponseAsync(responseMessage).ConfigureAwait(false);
                    returnValue.UserAgent = client.DefaultRequestHeaders.UserAgent.ToString();
                    return returnValue;
                }
            }
        }

        private static async Task<HttpResponse> CreateResponseAsync(HttpResponseMessage response)
        {
            var headers = new Dictionary<string, string>();
            if (response.Headers != null)
            {
                foreach (var kvp in response.Headers)
                {
                    headers[kvp.Key] = kvp.Value.First();
                }
            }

            return new HttpResponse
            {
                Headers = headers,
                Body = await response.Content.ReadAsStringAsync().ConfigureAwait(false),
                StatusCode = response.StatusCode
            };
        }
    }
}