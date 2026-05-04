#region
using Chaos.Client.Collections;
using Chaos.Client.Systems;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Models;

/// <summary>
///     Represents a visible entity in the game world. Tracked by <see cref="WorldState" /> and rendered by GameScreen.
///     Animation state is managed by <see cref="AnimationSystem" />.
/// </summary>
public sealed class WorldEntity
{
    public BodyAnimation? ActiveBodyAnimation { get; set; }

    //emote overlay — animated frame range in emot01.epf, -1 = no active emote
    public int ActiveEmoteFrame { get; set; } = -1;
    public float AnimElapsedMs { get; set; }
    public int AnimFrameCount { get; set; }
    public int AnimFrameIndex { get; set; }
    public float AnimFrameIntervalMs { get; set; }

    //animation state — managed by animationmanager
    public EntityAnimState AnimState { get; set; }

    /// <summary>
    ///     True when the entity is fully idle: <see cref="AnimState" /> is <see cref="EntityAnimState.Idle" /> AND no
    ///     emote overlay is playing. Movement initiation, turning, and emote-hotkey input are gated on this — emote
    ///     overlays (face emotes) are tracked independently from <see cref="AnimState" /> but still need to block input
    ///     just like body animations do.
    /// </summary>
    public bool IsAtRest => (AnimState == EntityAnimState.Idle) && (ActiveEmoteFrame < 0);

    //appearance
    public AislingAppearance? Appearance { get; set; }

    public int BodyAnimRepeatsLeft { get; set; }
    public CreatureType CreatureType { get; set; }
    public Direction Direction { get; set; }
    public float EmoteDurationMs { get; set; }
    public float EmoteElapsedMs { get; set; }
    public int EmoteFrameCount { get; set; }
    public float EmoteRemainingMs { get; set; }
    public int EmoteStartFrame { get; set; }

    /// <summary>
    ///     The gndattr paint height of the tile the entity is standing on. 0 = no ground tint. Controls how many pixels from
    ///     the entity bottom are tinted.
    /// </summary>
    public int GroundPaintHeight { get; set; }

    /// <summary>
    ///     The RGBA tint color from the tile's gndattr data. Applied to the lower portion of aislings.
    /// </summary>
    public Color GroundTintColor { get; set; }

    /// <summary>
    ///     Remaining duration in ms for the hit-flash tint. Ticked down each frame; the entity renders with HitTint while > 0.
    /// </summary>
    public float HitTintExpiryMs { get; set; }

    public string? GroupBoxText { get; set; }
    public uint Id { get; init; }
    public float IdleAnimElapsedMs { get; set; }

    //idle animation — frames per direction in "04" epf (0 = no idle anim)
    //idleanimtick increments independently of animstate so idle cycling survives body animations.
    public int IdleAnimFrameCount { get; set; }
    public int IdleAnimTick { get; set; }
    public int IdleCycleEndsAt { get; set; }

    //per-entity per-frame delay for idle animation (ms). 0 = use default. set per creature by
    //AnimationSystem.UpdateCreatureIdleCycle based on the creature's MpfIdleType.
    public float IdleFrameIntervalMs { get; set; }
    public bool IdleOptionalActive { get; set; }
    public bool IsDead { get; set; }

    /// <summary>
    ///     True when the entity is on a walk-blocking water tile (gndattr H=1) that triggers swimming animation.
    /// </summary>
    public bool IsOnSwimmingTile { get; set; }

    public bool IsTransparent { get; set; }
    public bool IsHidden { get; set; }
    public byte ItemColor { get; set; }
    public LanternSize LanternSize { get; set; }
    public string Name { get; set; } = string.Empty;
    public NameTagStyle NameTagStyle { get; set; }
    public RestPosition RestPosition { get; set; }
    public ushort SpriteId { get; set; }

    /// <summary>
    ///     Number of walk frames in the swim animation for this entity's gender. Set by UpdateEntityWaterState when on a
    ///     swimming tile. Used by AnimationSystem to determine walk duration while swimming.
    /// </summary>
    public int SwimWalkFrames { get; set; }

    //position
    public int TileX { get; set; }
    public int TileY { get; set; }
    public ClientEntityType Type { get; set; }
    public Vector2 VisualOffset { get; set; }
    public Vector2 WalkStartOffset { get; set; }

    public int SortDepth => TileX + TileY;

    /// <summary>
    ///     True when the entity is rendered as a creature sprite (either a native creature or an aisling in monster form).
    ///     False for normal aislings rendered via paperdoll composition.
    /// </summary>
    public bool IsRenderedAsCreatureSprite => (Type == ClientEntityType.Creature) || (Appearance is null && (SpriteId > 0));

    public bool UsesCreatureWalkTiming => IsRenderedAsCreatureSprite;
}