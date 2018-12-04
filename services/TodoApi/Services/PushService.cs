﻿using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Serilog;
using TodoApi.Settings;

namespace TodoApi.Services
{
    public class PushService : BackgroundService
    {
        private readonly ILogger<PushService> _logger;
        private readonly PushServerSettings _config;
        private readonly IdentityServerSettings _identityServerSettings;

        private CancellationToken _token;
        private HubConnection _connection;

        public PushService(
            ILogger<PushService> logger,
            IOptions<PushServerSettings> settingsAccessor,
            IOptions<IdentityServerSettings> identityServerSettings
        )
        {
            _logger = logger;
            _config = settingsAccessor.Value;
            _identityServerSettings = identityServerSettings.Value;
        }

        public async Task SendListCreated(int listId, string listName)
        {
            _logger?.LogDebug($"{nameof(SendListCreated)}: {{ListId}}, {{ListName}}", listId, listName);
            if (await EnsureConnected(_token))
            {
                await _connection.SendAsync("ListAdded", listId, listName, _token);
            }
        }

        public async Task SendListRenamed(int listId, string newName)
        {
            _logger?.LogDebug($"{nameof(SendListRenamed)}: {{ListId}}, {{ListName}}", listId, newName);
            if (await EnsureConnected(_token))
            {
                await _connection.SendAsync("ListRenamed", listId, newName, _token);
            }
        }

        public async Task SendListDeleted(int listId)
        {
            _logger?.LogDebug($"{nameof(SendListDeleted)}: {{ListId}}", listId);
            if (await EnsureConnected(_token))
            {
                await _connection.SendAsync("ListDeleted", listId, _token);
            }
        }

        public async Task SendItemAdded(int listId, int itemId, string itemName)
        {
            _logger?.LogDebug($"{nameof(SendItemAdded)}: {{ListId}}, {{ItemId}}, {{ItemName}}", listId, itemId, itemName);
            if (await EnsureConnected(_token))
            {
                await _connection.SendAsync("ItemAdded", listId, itemId, itemName, _token);
            }
        }

        public async Task SendItemNameChanged(int listId, int itemId, string newName)
        {
            _logger?.LogDebug($"{nameof(SendItemNameChanged)}: {{ListId}}, {{ItemId}}, {{ItemName}}", listId, itemId, newName);
            if (await EnsureConnected(_token))
            {
                await _connection.SendAsync("ItemNameChanged", listId, itemId, newName, _token);
            }
        }

        public async Task SendItemDoneChanged(int listId, int itemId, bool done)
        {
            _logger?.LogDebug($"{nameof(SendItemDoneChanged)}: {{ListId}}, {{ItemId}}, {{Done}}", listId, itemId, done);
            if (await EnsureConnected(_token))
            {
                await _connection.SendAsync("ItemDoneChanged", listId, itemId, done, _token);
            }
        }

        public async Task SendItemDeleted(int listId, int itemId)
        {
            _logger?.LogDebug($"{nameof(SendItemDeleted)}: {{ListId}}, {{ItemId}}", listId, itemId);
            if (await EnsureConnected(_token))
            {
                await _connection.SendAsync("ItemDeleted", listId, itemId, _token);
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Run( () => { _token = stoppingToken; }, stoppingToken);
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            await Connect(cancellationToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _connection?.StopAsync();

            _connection = null;

            await base.StopAsync(cancellationToken);
        }

        private async Task<bool> EnsureConnected(CancellationToken token)
        {
            if (_connection != null)
                return true;

            await Connect(token);
            return (_connection != null);
        }

        private async Task Connect(CancellationToken cancellationToken)
        {
            _logger?.LogDebug($"{nameof(Connect)}");
            var token = await GetToken(cancellationToken);

            if (String.IsNullOrWhiteSpace(token))
                return;

            var connection = new HubConnectionBuilder()
                .ConfigureLogging(lb => lb.AddSerilog())
                .WithUrl($"{_config.Url}/hubs/list?token=" + token)
                .Build();

            // This is only to prevent an error message in the logs, see https://github.com/aspnet/SignalR/issues/2313
            connection.On<int, int>("itemDeleted", (listId, itemId) => { });
            connection.On<int, int, bool>("itemDoneChanged", (listId, itemId, done) => { });
            connection.On<int, int, string>("itemNameChanged", (listId, itemId, newName) => { });
            connection.On<int, int, string>("itemAdded", (listId, itemId, name) => { });
            connection.On<int>("listDeleted", (listId) => { });
            connection.On<int, string>("listRenamed", (listId, newName) => { });
            connection.On<int, string>("listAdded", (listId, name) => { });

            connection.Closed += (e) => { _connection = null; return Task.CompletedTask; };

            _logger?.LogDebug($"{nameof(Connect)}: Trying to connect with token {{Token}}", token);

            await connection.StartAsync(cancellationToken).ConfigureAwait(false);
            _connection = connection;
        }

        private async Task<string> GetToken(CancellationToken token)
        {
            var client = new HttpClient();

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scopes", "pushapi"),
                new KeyValuePair<string, string>("client_id", _identityServerSettings.PushClientId),
                new KeyValuePair<string, string>("client_secret", _identityServerSettings.PushClientSecret),
            });

            var response = await client.PostAsync($"{_identityServerSettings.Url}/connect/token", content, token);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                dynamic tokenResponse = JsonConvert.DeserializeObject(responseContent);

                return tokenResponse.access_token;
            }

            return null;
        }
    }
}
