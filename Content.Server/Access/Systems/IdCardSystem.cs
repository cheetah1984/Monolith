using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Kitchen.Components;
using Content.Server.Popups;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Content.Server.Kitchen.EntitySystems;

namespace Content.Server.Access.Systems;

public sealed class IdCardSystem : SharedIdCardSystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly MicrowaveSystem _microwave = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<IdCardComponent, BeingMicrowavedEvent>(OnMicrowaved);
        SubscribeLocalEvent<IdCardComponent, ExaminedEvent>(OnExamined);
    }

    private void OnExamined(EntityUid uid, IdCardComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        // Show only company information if available
        if (!string.IsNullOrWhiteSpace(component.CompanyName) && component.CompanyName != "None")
        {
            if (_prototypeManager.TryIndex<CompanyPrototype>(component.CompanyName, out var companyProto))
            {
                args.PushMarkup($"[color={companyProto.Color.ToHex()}]{companyProto.Name}[/color]");
            }
            else
            {
                args.PushMarkup(component.CompanyName);
            }
        }
    }

    private void OnMicrowaved(EntityUid uid, IdCardComponent component, BeingMicrowavedEvent args)
    {
        if (!component.CanMicrowave || !TryComp<MicrowaveComponent>(args.Microwave, out var micro) || micro.Broken)
            return;

        if (TryComp<AccessComponent>(uid, out var access))
        {
            float randomPick = _random.NextFloat();

            // if really unlucky, burn card
            if (args.BeingHeated && randomPick <= 0.15f) // Frontier: if not being heated, don't destroy the ID
            {
                TryComp(uid, out TransformComponent? transformComponent);
                if (transformComponent != null)
                {
                    _popupSystem.PopupCoordinates(Loc.GetString("id-card-component-microwave-burnt", ("id", uid)),
                     transformComponent.Coordinates, PopupType.Medium);
                    EntityManager.SpawnEntity("FoodBadRecipe",
                        transformComponent.Coordinates);
                }
                _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(args.Microwave)} burnt {ToPrettyString(uid):entity}");
                EntityManager.QueueDeleteEntity(uid);
                return;
            }

            // Frontier: ID accesses only change with radiation
            if (!args.BeingIrradiated)
            {
                return;
            }
            // End Frontier

            //Explode if the microwave can't handle it
            if (!micro.CanMicrowaveIdsSafely)
            {
                _microwave.Explode((args.Microwave, micro));
                return;
            }

            // If they're unlucky, brick their ID
            if (randomPick <= 0.25f)
            {
                _popupSystem.PopupEntity(Loc.GetString("id-card-component-microwave-bricked", ("id", uid)), uid);

                access.Tags.Clear();
                Dirty(uid, access);

                _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(args.Microwave)} cleared access on {ToPrettyString(uid):entity}");
            }
            else
            {
                _popupSystem.PopupEntity(Loc.GetString("id-card-component-microwave-safe", ("id", uid)), uid, PopupType.Medium);
            }

            // Give them a wonderful new access to compensate for everything
            var random = _random.Pick(_prototypeManager.EnumeratePrototypes<AccessLevelPrototype>().ToArray());

            access.Tags.Add(random.ID);
            Dirty(uid, access);

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                    $"{ToPrettyString(args.Microwave)} added {random.ID} access to {ToPrettyString(uid):entity}");

        }
    }
}
