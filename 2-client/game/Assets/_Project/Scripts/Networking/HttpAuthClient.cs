using System;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Pogranichye.Client.Networking.Protocol;
using UnityEngine.Networking;

namespace Pogranichye.Client.Networking
{
    public sealed class HttpAuthClient
    {
        private readonly string _baseUrl;

        public HttpAuthClient(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
                throw new ArgumentException("Адрес сервера не задан.", nameof(baseUrl));

            _baseUrl = baseUrl.TrimEnd('/');
        }

        public Task<AuthResponse> RegisterAsync(string login, string password)
        {
            return PostAsync("/api/register", login, password);
        }

        public Task<AuthResponse> LoginAsync(string login, string password)
        {
            return PostAsync("/api/login", login, password);
        }

        private async Task<AuthResponse> PostAsync(string path, string login, string password)
        {
            var body = JsonConvert.SerializeObject(new AuthRequest
            {
                Login = login,
                Password = password
            });

            using (var request = new UnityWebRequest(_baseUrl + path, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.timeout = 15;

                var operation = request.SendWebRequest();
                var completion = new TaskCompletionSource<bool>();
                operation.completed += _ => completion.TrySetResult(true);
                await completion.Task;

                var responseText = request.downloadHandler?.text;
                if (string.IsNullOrWhiteSpace(responseText))
                    throw new InvalidOperationException(
                        $"Сервер вернул пустой ответ ({request.responseCode}): {request.error}");

                AuthResponse response;
                try
                {
                    response = JsonConvert.DeserializeObject<AuthResponse>(responseText);
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException("Ответ авторизации не является корректным JSON.", ex);
                }

                if (request.result != UnityWebRequest.Result.Success && response?.Ok != true)
                    throw new InvalidOperationException(response?.Message ?? request.error ?? "Ошибка HTTP.");

                return response ?? throw new InvalidOperationException("Пустой ответ авторизации.");
            }
        }
    }
}
