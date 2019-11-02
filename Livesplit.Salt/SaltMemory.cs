﻿using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace LiveSplit.Salt
{
    public class SaltMemory : IDisposable
    {
        private DateTime _nextHookAttempt = DateTime.MinValue;

        // PlayerMgr.Update
        private readonly ProgramPointer _players =
            new ProgramPointer(
                "558BEC5633F6A1????????837804007E253B700473238B4CB00880B9????????0074083909FF15????????46A1????????3970047FDB5E5DC3",
                7, 2);

        // MouseMgr.Update
        private readonly ProgramPointer _gameState =
            new ProgramPointer("7E1CA1????????8378040076758B7808C6472000C705????????00002041A1????????83F803731F", 31,
                1);

        // GameStateManager.GetMainCharacter
        private readonly ProgramPointer _characters =
            new ProgramPointer("85C07C168B15????????3942047E0B3B4204730E8B448208", 6, 2);

        private readonly Dictionary<int, Dictionary<string, InvLoot>> _playerItems =
            new Dictionary<int, Dictionary<string, InvLoot>>();
        private readonly Dictionary<int, DateTime> _playerItemTimes = new Dictionary<int, DateTime>();

        public Process Program { get; private set; }

        public bool IsHooked => Program != null && !Program.HasExited;

        public void Hook()
        {
            if (IsHooked || DateTime.Now < _nextHookAttempt)
            {
                return;
            }

            _nextHookAttempt = DateTime.Now.AddSeconds(1);

            Process[] processes = Process.GetProcessesByName("salt");
            if (processes.Length == 0)
            {
                return;
            }

            Program = processes[0];
            MemoryReader.Update64Bit(Program);
        }

        public int GetPlayerCount()
        {
            // PlayerMgr.player.Length
            return _players.Read<int>(Program, 0x4);
        }

        private void CheckPlayerIndex(int player)
        {
            if (player < 0 || player >= GetPlayerCount())
            {
                throw new ArgumentOutOfRangeException(nameof(player), player, "Invalid player index");
            }
        }

        public MenuLevel GetMenuLevel(int player)
        {
            CheckPlayerIndex(player);

            // PlayerMgr.player[player].menuMgr.curLevel
            return (MenuLevel) _players.Read<int>(Program, 0x8 + sizeof(uint) * player, 0x3C, 0x14);
        }

        public TransitionMode GetMenuTransitionMode(int player)
        {
            CheckPlayerIndex(player);

            // PlayerMgr.player[player].menuMgr.transMode
            return (TransitionMode) _players.Read<int>(Program, 0x8 + sizeof(uint) * player, 0x3C, 0x18);
        }

        public void UpdateInventory(int player)
        {
            CheckPlayerIndex(player);

            // 100ms cooldown between inventory updates
            if (!_playerItemTimes.TryGetValue(player, out DateTime time) || DateTime.Now > time)
            {
                _playerItemTimes[player] = DateTime.Now.AddMilliseconds(100);
            }
            else
            {
                return;
            }

            // Get player items list, clear
            if (!_playerItems.TryGetValue(player, out Dictionary<string, InvLoot> items))
            {
                items = new Dictionary<string, InvLoot>();
                _playerItems[player] = items;
            }

            items.Clear();

            // PlayerMgr.player[player].playerInv.inventory
            IntPtr inventory = (IntPtr) _players.Read<uint>(Program, 0x8 + sizeof(uint) * player, 0x24, 0x8);

            int len = Program.Read<int>(inventory, 0x4);
            for (int i = 0; i < len; i++)
            {
                // inventory[i]
                IntPtr itemPtr = (IntPtr) Program.Read<uint>(inventory, 0x8 + sizeof(uint) * i);

                if (itemPtr == IntPtr.Zero)
                {
                    continue;
                }

                // Follow ptr to struct
                InvLoot item = new InvLoot(Program, itemPtr);

                if (item.name != null)
                {
                    items[item.name] = item;
                }
            }
        }

        public bool TryGetPlayerItem(int player, string itemName, out InvLoot item)
        {
            CheckPlayerIndex(player);
            UpdateInventory(player);

            return _playerItems[player].TryGetValue(itemName, out item);
        }

        public bool IsGameEnding()
        {
            // PlayerMgr.player[0].namelessEndFrame
            bool dominion = _players.Read<float>(Program, 0x8, 0x138) > 0;

            // PlayerMgr.player[0].charIdx
            int charIndex = _players.Read<int>(Program, 0x8, 0x9C);

            // CharMgr.character[charIndex].loc.Y
            float charY = _characters.Read<float>(Program, 0x8 + sizeof(uint) * charIndex, 0xD8);

            return dominion || charY > 47900;
        }

        public GameState GetGameState()
        {
            return (GameState) _gameState.Read<int>(Program, 0x0);
        }

        public void Dispose()
        {
            Program?.Dispose();
        }
    }
}
