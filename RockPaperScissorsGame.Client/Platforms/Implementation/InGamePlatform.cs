﻿using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RockPaperScissorsGame.Client.Platforms.Abstract;
using RockPaperScissorsGame.Client.Platforms.Base;
using RockPaperScissorsGame.Client.Services.Abstract;
using RockPaperScissorsGame.Client.Settings;
using RockPaperScissorsGame.Common;

namespace RockPaperScissorsGame.Client.Platforms.Implementation
{
    public class InGamePlatform : BasePlatform, IInGamePlatform
    {
        private bool _isWaiting;
        private bool _isConnectionConfigured;
        private Timer _keepSessionActiveTimer;

        private readonly IInGameService _inGameService;
        private readonly IConnectionService _connectionService;
        private readonly IOptions<TimeoutSettings> _timeoutSettings;
        private readonly ILogger<InGamePlatform> _logger;

        public InGamePlatform(IInGameService inGameService,
                                IConnectionService connectionService, 
                                IOptions<TimeoutSettings> timeoutSettings, 
                                ILogger<InGamePlatform> logger)
        {
            _inGameService = inGameService;
            _connectionService = connectionService;
            _timeoutSettings = timeoutSettings;
            _logger = logger;
        }

        private async Task<bool> EnsureConnectionConfigured()
        {
            if (!await _connectionService.EnsureConnectionAsync(PlayerId)) return false;
            if (_isConnectionConfigured) return true;
            
            _connectionService.Connection.On(GameEvents.GameStart, () =>
            {
                _isWaiting = false;
            });

            _connectionService.Connection.On(GameEvents.GameClosed, () =>
            {
                if (KeepProgramActive)
                {
                    Console.WriteLine("\nPress 'Enter' to continue...");
                }
                KeepProgramActive = false;
            });
            
            _connectionService.Connection.On<string>(GameEvents.GameEnd, (result) =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n\nRound summary\n{result}");
                Console.ResetColor();
            });
                
            _connectionService.Connection.On(GameEvents.GameAborted, () =>
            {
                Console.WriteLine("\nPress 'Enter' to continue...");
                ShouldSkipNextInstruction = true;
            });
            
            _isConnectionConfigured = true;
            return true;
        }
        
        private async Task MakeMove()
        {
            _logger.LogInformation($"{nameof(InGamePlatform)}: Start of making move");

            if (!await EnsureConnectionConfigured())
            {
                _logger.LogError($"{nameof(InGamePlatform)}: Unable to connect to the server");

                return;
            }
            
            bool isMoveMadeInTime = true;
            var timer = new Timer(
                new TimerCallback(async state =>
                {
                    isMoveMadeInTime = false;
                    _logger.LogInformation($"{nameof(InGamePlatform)}: Move time expired");
                    await _inGameService.MakeMoveAsync(PlayerId, MoveOptions.Undefined, isMoveMadeInTime);
                }),
                null,
                _timeoutSettings.Value?.MoveTimeout ?? 20000,
                Timeout.Infinite
                );
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine();
            Console.WriteLine("1. Rock");
            Console.WriteLine("2. Paper"); 
            Console.WriteLine("3. Scissors");
            Console.ResetColor();

            MoveOptions figure = MoveOptions.Undefined;
            while (true)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write("Choose your figure: ");
                Console.ResetColor();
                
                string userInput = Console.ReadLine();

                if (!isMoveMadeInTime)
                {
                    break;
                }

                if (!KeepProgramActive)
                {
                    await timer.DisposeAsync();
                    return;
                }
                
                if (string.IsNullOrEmpty(userInput))
                {
                    Console.WriteLine("Empty input. Try again\n");
                    continue;
                }

                if (!int.TryParse(userInput.Trim(), out int figureNumber) || figureNumber < 1 || figureNumber > 3 || Enum.TryParse(userInput, out figure) == false)
                {
                    Console.WriteLine("Unknown figure. Try again\n");
                    continue;
                }
                
                break;
            }
            
            if (isMoveMadeInTime)
            {
                await timer.DisposeAsync();
                _keepSessionActiveTimer.Change(_timeoutSettings.Value?.SeriesTimeout ?? 300000, Timeout.Infinite);
                _logger.LogInformation($"{nameof(InGamePlatform)}: Move made in time");
                await _inGameService.MakeMoveAsync(PlayerId, figure, isMoveMadeInTime);
            } 
        }

        private async Task Exit()
        {
            KeepProgramActive = false;
            await _inGameService.LeaveGameAsync(PlayerId);
        }

        public async Task StartAsync(int waitTimeSecs)
        {
            _keepSessionActiveTimer = new Timer(
                new TimerCallback(async state =>
                {
                    if (KeepProgramActive)
                    {
                        KeepProgramActive = false;
                        await _inGameService.LeaveGameAsync(PlayerId);
                    }
                }),
                null,
                _timeoutSettings.Value?.SeriesTimeout ?? 300000,
                Timeout.Infinite
            );

            
            KeepProgramActive = true;
            if (waitTimeSecs > 0)
            {
                _isWaiting = true;
            }
            
            if (!await EnsureConnectionConfigured())
            {
                return;
            }
            
            if (_isWaiting && KeepProgramActive)
            {
                for (int i = 0; i < waitTimeSecs/2; i++)
                {
                    await Task.Delay(2000);
                    if (_isWaiting == false || KeepProgramActive == false)
                    {
                        break;
                    }
                }

                if (_isWaiting || KeepProgramActive == false)
                {
                    await Exit();
                    return;
                }
            }
            
            await base.StartAsync(PlayerId);
        }

        protected override async Task<bool> ChooseCommandAsync(int commandNumber)
        {
            bool correctCommand = true;
            switch (commandNumber)
            {
                case 1:
                    await MakeMove();
                    break;
                case 0:
                    await Exit();
                    break;
                default:
                    correctCommand = false;
                    break;
            }
            
            return correctCommand;
        }
        
        protected override async Task PrintUserMenu()
        {
            await Task.Delay(500);
            Console.ForegroundColor = ConsoleColor.Green;

            Console.WriteLine(); 
            Console.WriteLine("1. Make move");
            Console.WriteLine("0. Leave game");
            
            Console.ResetColor();
        }

    }
}