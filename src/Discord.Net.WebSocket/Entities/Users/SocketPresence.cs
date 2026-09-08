using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Model = Discord.API.Presence;

namespace Discord.WebSocket
{
    /// <summary>
    ///     Represents the WebSocket user's presence status. This may include their online status and their activity.
    /// </summary>
    [DebuggerDisplay(@"{DebuggerDisplay,nq}")]
    public class SocketPresence : IPresence
    {
        /// <inheritdoc />
        public UserStatus Status { get; private set; }
        /// <inheritdoc />
        public IReadOnlyCollection<ClientType> ActiveClients { get; private set; }
        /// <inheritdoc />
        public IReadOnlyCollection<IActivity> Activities { get; private set; }

        internal SocketPresence() { }
        internal SocketPresence(UserStatus status, IImmutableSet<ClientType> activeClients, IImmutableList<IActivity> activities)
        {
            Status = status;
            ActiveClients = activeClients ?? ImmutableHashSet<ClientType>.Empty;
            Activities = activities ?? ImmutableList<IActivity>.Empty;
        }

        internal static SocketPresence Create(Model model)
        {
            var entity = new SocketPresence();
            entity.Update(model);
            return entity;
        }

        /// <summary>
        ///     Applies the presence frame, returning whether anything actually changed.
        /// </summary>
        /// <remarks>
        ///     Discord sends one presence frame per mutual guild, so the same presence arrives once for every guild
        ///     the user shares with the client. Comparing against the model before converting it lets the redundant
        ///     frames be dropped without building any entities.
        /// </remarks>
        internal bool Update(Model model)
        {
            var hasChanges = false;

            if (Status != model.Status)
            {
                Status = model.Status;
                hasChanges = true;
            }

            var clientStatus = model.ClientStatus.GetValueOrDefault();
            if (!ClientTypesEqual(ActiveClients, clientStatus))
            {
                ActiveClients = ConvertClientTypesDict(clientStatus);
                hasChanges = true;
            }

            if (!ActivitiesEqual(Activities, model.Activities))
            {
                Activities = ConvertActivitiesList(model.Activities);
                hasChanges = true;
            }

            return hasChanges;
        }

        private static bool ClientTypesEqual(IReadOnlyCollection<ClientType> current, IDictionary<string, string> clientTypesDict)
        {
            if (current == null)
                return false;
            if (clientTypesDict == null || clientTypesDict.Count == 0)
                return current.Count == 0;

            var count = 0;
            foreach (var key in clientTypesDict.Keys)
            {
                if (!Enum.TryParse(key, true, out ClientType type))
                    continue;
                if (!current.Contains(type))
                    return false;
                count++;
            }
            return count == current.Count;
        }

        private static bool ActivitiesEqual(IReadOnlyCollection<IActivity> current, IList<API.Game> models)
        {
            if (current == null)
                return false;
            if (models == null || models.Count == 0)
                return current.Count == 0;
            if (current.Count != models.Count)
                return false;

            var index = 0;
            foreach (var activity in current)
            {
                if (!ActivityEquals(activity, models[index++]))
                    return false;
            }
            return true;
        }

        private static bool ActivityEquals(IActivity activity, API.Game model)
        {
            if (activity == null || activity.Name != model.Name)
                return false;

            switch (activity)
            {
                case CustomStatusGame custom:
                    return model.Id.GetValueOrDefault() == "custom"
                        && custom.State == model.State.GetValueOrDefault()
                        && custom.Emote?.Name == (model.Emoji.IsSpecified ? model.Emoji.Value.Name : null);
                case SpotifyGame spotify:
                    return model.SyncId.IsSpecified
                        && spotify.TrackId == model.SyncId.Value
                        && spotify.TrackTitle == model.Details.GetValueOrDefault();
                case RichGame rich:
                    return model.ApplicationId.IsSpecified
                        && rich.ApplicationId == model.ApplicationId.Value
                        && rich.Details == model.Details.GetValueOrDefault()
                        && rich.State == model.State.GetValueOrDefault();
                case StreamingGame streaming:
                    return model.StreamUrl.IsSpecified
                        && streaming.Url == model.StreamUrl.Value
                        && streaming.Details == model.Details.GetValueOrDefault();
                case Game game:
                    return model.Id.GetValueOrDefault() != "custom"
                        && !model.SyncId.IsSpecified
                        && !model.ApplicationId.IsSpecified
                        && !model.StreamUrl.IsSpecified
                        && game.Type == (model.Type.GetValueOrDefault() ?? ActivityType.Playing)
                        && game.Details == model.Details.GetValueOrDefault();
                default:
                    return false;
            }
        }

        /// <summary>
        ///     Creates a new <see cref="IReadOnlyCollection{T}"/> containing all of the client types
        ///     where a user is active from the data supplied in the Presence update frame.
        /// </summary>
        /// <param name="clientTypesDict">
        ///     A dictionary keyed by the <see cref="ClientType"/>
        ///     and where the value is the <see cref="UserStatus"/>.
        /// </param>
        /// <returns>
        ///     A collection of all <see cref="ClientType"/>s that this user is active.
        /// </returns>
        private static IReadOnlyCollection<ClientType> ConvertClientTypesDict(IDictionary<string, string> clientTypesDict)
        {
            if (clientTypesDict == null || clientTypesDict.Count == 0)
                return ImmutableHashSet<ClientType>.Empty;
            var builder = ImmutableHashSet.CreateBuilder<ClientType>();
            foreach (var key in clientTypesDict.Keys)
            {
                if (Enum.TryParse(key, true, out ClientType type))
                    builder.Add(type);
                // quietly discard ClientTypes that do not match
            }
            return builder.ToImmutable();
        }
        /// <summary>
        ///     Creates a new <see cref="IReadOnlyCollection{T}"/> containing all the activities
        ///     that a user has from the data supplied in the Presence update frame.
        /// </summary>
        /// <param name="activities">
        ///     A list of <see cref="API.Game"/>.
        /// </param>
        /// <returns>
        ///     A list of all <see cref="IActivity"/> that this user currently has available.
        /// </returns>
        private static IReadOnlyCollection<IActivity> ConvertActivitiesList(IList<API.Game> activities)
        {
            if (activities == null || activities.Count == 0)
                return ImmutableArray<IActivity>.Empty;
            var builder = ImmutableArray.CreateBuilder<IActivity>(activities.Count);
            foreach (var activity in activities)
                builder.Add(activity.ToEntity());
            return builder.MoveToImmutable();
        }

        /// <summary>
        ///     Gets the status of the user.
        /// </summary>
        /// <returns>
        ///     A string that resolves to <see cref="Discord.WebSocket.SocketPresence.Status" />.
        /// </returns>
        public override string ToString() => Status.ToString();
        private string DebuggerDisplay => $"{Status}{(Activities?.FirstOrDefault()?.Name ?? "")}";

        internal SocketPresence Clone() => MemberwiseClone() as SocketPresence;
    }
}
