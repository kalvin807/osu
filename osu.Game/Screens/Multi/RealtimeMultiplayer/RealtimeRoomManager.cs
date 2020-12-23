// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Extensions;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.RoomStatuses;
using osu.Game.Online.RealtimeMultiplayer;
using osu.Game.Screens.Multi.Components;

namespace osu.Game.Screens.Multi.RealtimeMultiplayer
{
    public class RealtimeRoomManager : RoomManager
    {
        [Resolved]
        private StatefulMultiplayerClient multiplayerClient { get; set; }

        public readonly Bindable<double> TimeBetweenListingPolls = new Bindable<double>();
        public readonly Bindable<double> TimeBetweenSelectionPolls = new Bindable<double>();
        private readonly IBindable<bool> isConnected = new Bindable<bool>();
        private readonly Bindable<bool> allowPolling = new Bindable<bool>();

        private ListingPollingComponent listingPollingComponent;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            isConnected.BindTo(multiplayerClient.IsConnected);
            isConnected.BindValueChanged(_ => Schedule(updatePolling));
            JoinedRoom.BindValueChanged(_ => updatePolling());

            updatePolling();
        }

        public override void CreateRoom(Room room, Action<Room> onSuccess = null, Action<string> onError = null)
            => base.CreateRoom(room, r => joinMultiplayerRoom(r, onSuccess, onError), onError);

        public override void JoinRoom(Room room, Action<Room> onSuccess = null, Action<string> onError = null)
        {
            if (!multiplayerClient.IsConnected.Value)
            {
                onError?.Invoke("Not currently connected to the multiplayer server.");
                return;
            }

            // this is done here as a pre-check to avoid clicking on already closed rooms in the lounge from triggering a server join.
            // should probably be done at a higher level, but due to the current structure of things this is the easiest place for now.
            if (room.Status.Value is RoomStatusEnded)
            {
                onError?.Invoke("Cannot join an ended room.");
                return;
            }

            base.JoinRoom(room, r => joinMultiplayerRoom(r, onSuccess, onError), onError);
        }

        public override void PartRoom()
        {
            if (JoinedRoom.Value == null)
                return;

            var joinedRoom = JoinedRoom.Value;

            base.PartRoom();

            multiplayerClient.LeaveRoom().FireAndForget();

            // Todo: This is not the way to do this. Basically when we're the only participant and the room closes, there's no way to know if this is actually the case.
            // This is delayed one frame because upon exiting the match subscreen, multiplayer updates the polling rate and messes with polling.
            Schedule(() =>
            {
                RemoveRoom(joinedRoom);
                listingPollingComponent.PollImmediately();
            });
        }

        private void joinMultiplayerRoom(Room room, Action<Room> onSuccess = null, Action<string> onError = null)
        {
            Debug.Assert(room.RoomID.Value != null);

            multiplayerClient.JoinRoom(room).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                    Schedule(() => onSuccess?.Invoke(room));
                else
                {
                    if (t.Exception != null)
                        Logger.Error(t.Exception, "Failed to join multiplayer room.");

                    PartRoom();
                    Schedule(() => onError?.Invoke(t.Exception?.ToString() ?? string.Empty));
                }
            });
        }

        private void updatePolling()
        {
            if (!isConnected.Value)
                ClearRooms();

            // Don't poll when not connected or when a room has been joined.
            allowPolling.Value = isConnected.Value && JoinedRoom.Value == null;
        }

        protected override IEnumerable<RoomPollingComponent> CreatePollingComponents() => new RoomPollingComponent[]
        {
            listingPollingComponent = new RealtimeListingPollingComponent
            {
                TimeBetweenPolls = { BindTarget = TimeBetweenListingPolls },
                AllowPolling = { BindTarget = allowPolling }
            },
            new RealtimeSelectionPollingComponent
            {
                TimeBetweenPolls = { BindTarget = TimeBetweenSelectionPolls },
                AllowPolling = { BindTarget = allowPolling }
            }
        };

        private class RealtimeListingPollingComponent : ListingPollingComponent
        {
            public readonly IBindable<bool> AllowPolling = new Bindable<bool>();

            protected override void LoadComplete()
            {
                base.LoadComplete();

                AllowPolling.BindValueChanged(allowPolling =>
                {
                    if (!allowPolling.NewValue)
                        return;

                    if (IsLoaded)
                        PollImmediately();
                });
            }

            protected override Task Poll() => !AllowPolling.Value ? Task.CompletedTask : base.Poll();
        }

        private class RealtimeSelectionPollingComponent : SelectionPollingComponent
        {
            public readonly IBindable<bool> AllowPolling = new Bindable<bool>();

            protected override void LoadComplete()
            {
                base.LoadComplete();

                AllowPolling.BindValueChanged(allowPolling =>
                {
                    if (!allowPolling.NewValue)
                        return;

                    if (IsLoaded)
                        PollImmediately();
                });
            }

            protected override Task Poll() => !AllowPolling.Value ? Task.CompletedTask : base.Poll();
        }
    }
}
