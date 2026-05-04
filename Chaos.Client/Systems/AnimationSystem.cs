#region
using Chaos.Client.Data.Utilities;
using Chaos.Client.Models;
using Chaos.DarkAges.Definitions;
using Chaos.Geometry.Abstractions.Definitions;
using DALib.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Systems;

/// <summary>
///     Static class containing all entity animation logic. WorldEntity is a data bag; this class provides pure methods to
///     start, advance, and resolve animation state for rendering.
/// </summary>
public static class AnimationSystem
{
    private const int DEFAULT_WALK_FRAMES = 4;
    private const float DEFAULT_WALK_FRAME_MS = 114;
    private const float REMOTE_AISLING_WALK_FRAME_MS = 100f;
    private const float CREATURE_WALK_FRAME_MS = 100f;
    private const float MORPHED_AISLING_WALK_FRAME_MS = 75f;
    private const float MIN_WALK_DURATION_MS = 75f;

    //the server's animationspeed value × 10 = total animation duration in ms.
    //smaller speed = faster animation (e.g. speed=25 → 250ms, speed=100 → 1000ms).
    private const float BODY_ANIM_SPEED_TO_MS = 10f;
    private const float DEFAULT_BODY_ANIM_FRAME_MS = 150f;
    private const float CREATURE_ATTACK_FRAME_MS = 300f;

    //fallback per-frame delay for idle animation (ms). applies to aislings and to uninitialized
    //entities; creature intervals come directly from CreatureAnimInfo.AnimationIntervalMs.
    private const float DEFAULT_IDLE_FRAME_MS = 300f;

    //aisling walk anim "01": frame 0 = up idle, 1-4 = up walk; frame 5 = right idle, 6-9 = right walk
    private const int AISLING_UP_WALK_BASE = 1;
    private const int AISLING_RIGHT_WALK_BASE = 6;

    #region Start
    /// <summary>
    ///     Sets up walk animation state on an entity. Call after updating tile position.
    /// </summary>
    public static void StartWalk(
        WorldEntity entity,
        Direction direction,
        bool isCreature = false,
        bool isLocalPlayer = false,
        int? walkFrameOverride = null)
    {
        //facing direction always matches walk direction. setting it here keeps entity.Direction
        //and WalkStartOffset locked in lockstep, preventing the sprite from facing one way while
        //the screen scrolls another (e.g. when the server rubberbands the player into a walk
        //direction the client did not predict).
        entity.Direction = direction;

        //swimming is not a form — use swim animation frame count and normal walk speed on water tiles
        var swimming = entity is { IsOnSwimmingTile: true, SwimWalkFrames: > 0 };

        var frameCount = swimming ? entity.SwimWalkFrames : walkFrameOverride ?? DEFAULT_WALK_FRAMES;

        entity.AnimState = EntityAnimState.Walking;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = frameCount;

        entity.AnimFrameIntervalMs = isCreature && !swimming
            ? entity.Type == ClientEntityType.Aisling ? MORPHED_AISLING_WALK_FRAME_MS : CREATURE_WALK_FRAME_MS
            : isLocalPlayer
                ? DEFAULT_WALK_FRAME_MS
                : REMOTE_AISLING_WALK_FRAME_MS;

        entity.WalkStartOffset = GetWalkOffset(direction);
        entity.VisualOffset = entity.WalkStartOffset;
    }

    /// <summary>
    ///     Sets up a body animation (attack/cast) on an aisling entity. Ignores if currently walking.
    /// </summary>
    public static void StartBodyAnimation(WorldEntity entity, BodyAnimation bodyAnim, ushort animSpeed)
    {
        if (entity.AnimState == EntityAnimState.Walking)
            return;

        (_, var framesPerDir, _, _) = ResolveBodyAnimParams(bodyAnim);

        if (framesPerDir == 0)
            return;

        var repeats = bodyAnim switch
        {
            BodyAnimation.Wave => 1,
            _                  => 0
        };

        float frameIntervalMs;

        //emotes with body animations use total duration from the emote table, divided across frames and repeats
        if (DataUtilities.IsEmote(bodyAnim))
        {
            (_, _, var emoteDuration) = ResolveEmoteFrames(bodyAnim);
            frameIntervalMs = (emoteDuration > 0 ? emoteDuration : DEFAULT_BODY_ANIM_FRAME_MS) / framesPerDir / (1 + repeats);
        } else

            //non-emote body animations: animspeed * 10 = per-frame interval
            frameIntervalMs = animSpeed > 0 ? animSpeed * BODY_ANIM_SPEED_TO_MS : DEFAULT_BODY_ANIM_FRAME_MS;

        entity.AnimState = EntityAnimState.BodyAnim;
        entity.ActiveBodyAnimation = bodyAnim;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = framesPerDir;
        entity.AnimFrameIntervalMs = frameIntervalMs;
        entity.BodyAnimRepeatsLeft = repeats;
    }

    /// <summary>
    ///     Starts an attack animation on a creature entity.
    /// </summary>
    /// <remarks>
    ///     The <see cref="BodyAnimation" /> is mapped to an attack index — Kick(131)→Attack2, RoundHouseKick(133)→Attack3,
    ///     everything else→Attack1 — and falls back to Attack1 when the mapped attack has no frames. Per-frame display time
    ///     for creature attacks is fixed at 300 ms.
    /// </remarks>
    public static void StartCreatureBodyAnimation(
        WorldEntity entity,
        BodyAnimation bodyAnim,
        ushort animSpeed,
        in CreatureAnimInfo animInfo)
    {
        if (entity.AnimState == EntityAnimState.Walking)
            return;

        if (DataUtilities.IsEmote(bodyAnim))
            return;

        (_, var framesPerDir) = ResolveCreatureAttack(bodyAnim, in animInfo);

        if (framesPerDir == 0)
            return;

        entity.AnimState = EntityAnimState.BodyAnim;
        entity.ActiveBodyAnimation = bodyAnim;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.AnimFrameCount = framesPerDir;
        entity.AnimFrameIntervalMs = CREATURE_ATTACK_FRAME_MS;
    }

    /// <summary>
    ///     Maps a BodyAnimation to the correct creature attack range. Falls back to Attack1 if the mapped attack has no
    ///     frames.
    /// </summary>
    private static (byte StartIndex, byte FrameCount) ResolveCreatureAttack(BodyAnimation bodyAnim, in CreatureAnimInfo info)
    {
        (var startIndex, var frameCount) = bodyAnim switch
        {
            BodyAnimation.Kick when info.Attack2FrameCount > 0           => (info.Attack2StartIndex, info.Attack2FrameCount),
            BodyAnimation.RoundHouseKick when info.Attack3FrameCount > 0 => (info.Attack3StartIndex, info.Attack3FrameCount),
            _                                                            => (info.AttackFrameIndex, info.AttackFrameCount)
        };

        return (startIndex, frameCount);
    }
    #endregion

    #region Advance
    /// <summary>
    ///     Advances an entity's animation by the given elapsed milliseconds. Handles walk offset, frame stepping, and
    ///     completion reset to idle.
    /// </summary>
    /// <param name="entity">The entity whose animation state to advance.</param>
    /// <param name="elapsedMs">Milliseconds elapsed since the last frame.</param>
    /// <param name="smoothScroll">
    ///     When true, the player's walk visual offset lerps continuously between frames. When false (default),
    ///     the visual offset steps discretely with each animation frame, matching how all non-player entities move.
    /// </param>
    public static void Advance(WorldEntity entity, float elapsedMs, bool smoothScroll = false)
    {
        //idle animation ticks independently so it survives body animations.
        //aislings use idleanimframecount (from "04" epf); creatures always tick.
        //swimming aislings always tick so the swim animation cycles while idle.
        if ((entity.IdleAnimFrameCount > 0) || (entity.Type == ClientEntityType.Creature) || entity.IsOnSwimmingTile)
            AdvanceIdleAnim(entity, elapsedMs);

        switch (entity.AnimState)
        {
            case EntityAnimState.Walking:
                AdvanceWalk(entity, elapsedMs, smoothScroll);

                break;

            case EntityAnimState.BodyAnim:
                AdvanceBodyAnim(entity, elapsedMs);

                break;
        }
    }

    private static void AdvanceWalk(WorldEntity entity, float elapsedMs, bool smoothScroll)
    {
        entity.AnimElapsedMs += elapsedMs;

        //total duration includes all frames — each frame gets a full interval (including the last).
        var totalDuration = Math.Max(MIN_WALK_DURATION_MS, entity.AnimFrameCount * entity.AnimFrameIntervalMs);
        var progress = Math.Clamp(entity.AnimElapsedMs / totalDuration, 0f, 1f);

        //some entities have no walk frames — that's valid. the walk slide still needs to play out,
        //but there's no frame stepping to do.
        entity.AnimFrameIndex = entity.AnimFrameCount > 0
            ? Math.Clamp((int)(progress * entity.AnimFrameCount), 0, entity.AnimFrameCount - 1)
            : 0;

        //both smooth and stepped use integer-only offsets to prevent sub-pixel wobble.
        //the walk start offsets are always integer (±28, ±14), and integer division
        //ensures every intermediate value is also integer.
        if (smoothScroll)
        {
            //smooth: interpolate at 2x the stepped frame rate (double the visual steps).
            var smoothFrameCount = entity.AnimFrameCount * 2;
            var smoothFrameIndex = smoothFrameCount > 0
                ? Math.Clamp((int)(progress * smoothFrameCount), 0, smoothFrameCount - 1)
                : 0;
            entity.VisualOffset = GetSteppedWalkOffset(entity.WalkStartOffset, smoothFrameIndex, smoothFrameCount);
        } else

            //stepped: offset jumps discretely with each animation frame
            entity.VisualOffset = GetSteppedWalkOffset(entity.WalkStartOffset, entity.AnimFrameIndex, entity.AnimFrameCount);

        if (progress >= 1f)
            ResetToIdle(entity);
    }

    private static void AdvanceIdleAnim(WorldEntity entity, float elapsedMs)
    {
        //per-entity interval is set by UpdateCreatureIdleCycle for creatures; aislings and
        //pre-seeded entities fall through to the default 300ms tick rate.
        var interval = entity.IdleFrameIntervalMs > 0f ? entity.IdleFrameIntervalMs : DEFAULT_IDLE_FRAME_MS;

        entity.IdleAnimElapsedMs += elapsedMs;

        while (entity.IdleAnimElapsedMs >= interval)
        {
            entity.IdleAnimTick++;
            entity.IdleAnimElapsedMs -= interval;
        }
    }

    /// <summary>
    ///     Updates a creature's idle animation cycle, selecting the per-frame interval and rolling a new active loop when
    ///     the previous one completes.
    /// </summary>
    /// <param name="entity">The creature whose idle cycle to advance.</param>
    /// <param name="info">The creature's animation metadata.</param>
    /// <remarks>
    ///     Per-frame timing comes directly from <see cref="CreatureAnimInfo.AnimationIntervalMs" />; cycle selection is
    ///     chosen from <see cref="CreatureAnimInfo.IdleType" />. Intended to be called once per update after
    ///     <see cref="Advance" />.
    /// </remarks>
    public static void UpdateCreatureIdleCycle(WorldEntity entity, in CreatureAnimInfo info)
    {
        entity.IdleFrameIntervalMs = info.AnimationIntervalMs;

        switch (info.IdleType)
        {
            case MpfIdleType.StaticNoIdle:
                entity.IdleOptionalActive = false;
                entity.IdleCycleEndsAt = int.MaxValue;

                return;

            case MpfIdleType.NormalIdle:
                entity.IdleOptionalActive = true;

                if (entity.IdleAnimTick < entity.IdleCycleEndsAt)
                    return;

                entity.IdleCycleEndsAt = entity.IdleAnimTick + Math.Max((int)info.OptionalAnimationFrameCount, 1);

                return;

            case MpfIdleType.NormalPlusOptional:
                if (entity.IdleAnimTick < entity.IdleCycleEndsAt)
                    return;

                entity.IdleOptionalActive = (info.OptionalAnimationProbability > 0)
                                            && (Random.Shared.Next(100) < info.OptionalAnimationProbability);

                int cycleLength = entity.IdleOptionalActive ? info.OptionalAnimationFrameCount : info.StandingFrameCount;
                entity.IdleCycleEndsAt = entity.IdleAnimTick + Math.Max(cycleLength, 1);

                return;
        }
    }

    private static void AdvanceBodyAnim(WorldEntity entity, float elapsedMs)
    {
        entity.AnimElapsedMs += elapsedMs;

        while (entity.AnimElapsedMs >= entity.AnimFrameIntervalMs)
        {
            entity.AnimFrameIndex++;
            entity.AnimElapsedMs -= entity.AnimFrameIntervalMs;
        }

        if (entity.AnimFrameIndex >= entity.AnimFrameCount)
        {
            if (entity.BodyAnimRepeatsLeft > 0)
            {
                entity.BodyAnimRepeatsLeft--;
                entity.AnimFrameIndex = 0;
            } else
                ResetToIdle(entity);
        }
    }

    public static void ResetToIdle(WorldEntity entity)
    {
        entity.AnimState = EntityAnimState.Idle;
        entity.ActiveBodyAnimation = null;
        entity.AnimFrameIndex = 0;
        entity.AnimElapsedMs = 0;
        entity.VisualOffset = Vector2.Zero;
    }

    /// <summary>
    ///     Immediately terminates any walk, body animation, and emote overlay on the entity. Leaves the idle cycle
    ///     running. Callers must update position/direction/appearance first — zeroing <see cref="WorldEntity.VisualOffset"/>
    ///     snaps the entity to whatever tile <see cref="WorldEntity.TileX"/>/<see cref="WorldEntity.TileY"/> currently holds.
    /// </summary>
    public static void CancelAllAnimations(WorldEntity entity)
    {
        ResetToIdle(entity);

        entity.BodyAnimRepeatsLeft = 0;

        entity.ActiveEmoteFrame = -1;
        entity.EmoteElapsedMs = 0;
        entity.EmoteRemainingMs = 0;
        entity.EmoteDurationMs = 0;
        entity.EmoteStartFrame = 0;
        entity.EmoteFrameCount = 0;
    }
    #endregion

    #region Frame Resolution
    /// <summary>
    ///     Returns the correct creature sprite frame index and flip flag for the entity's current animation state.
    /// </summary>
    public static (int FrameIndex, bool Flip) GetCreatureFrame(WorldEntity entity, in CreatureAnimInfo info)
    {
        switch (entity.AnimState)
        {
            case EntityAnimState.Walking:
            {
                var framesPerDir = info.WalkFrameCount;

                if (framesPerDir == 0)
                    return GetCreatureIdleFrame(entity, in info);

                var mappedFrame = MapFrameIndex(entity.AnimFrameIndex, entity.AnimFrameCount, framesPerDir);

                //single-direction sprite: reuse base frames for all directions
                var walkOffset = (info.WalkFrameIndex + framesPerDir + mappedFrame) >= info.TotalFrameCount ? 0 : framesPerDir;

                return entity.Direction switch
                {
                    Direction.Up    => (info.WalkFrameIndex + mappedFrame, false),
                    Direction.Right => (info.WalkFrameIndex + walkOffset + mappedFrame, false),
                    Direction.Down  => (info.WalkFrameIndex + walkOffset + mappedFrame, true),
                    Direction.Left  => (info.WalkFrameIndex + mappedFrame, true),
                    _               => (info.WalkFrameIndex + mappedFrame, false)
                };
            }

            case EntityAnimState.BodyAnim:
            {
                //use the correct attack range based on the active bodyanimation
                (var attackStart, var framesPerDir) = ResolveCreatureAttack(entity.ActiveBodyAnimation ?? BodyAnimation.Assail, in info);

                if (framesPerDir == 0)
                    return GetCreatureIdleFrame(entity, in info);

                var animFrame = Math.Min(entity.AnimFrameIndex, framesPerDir - 1);

                //single-direction sprite: reuse base frames for all directions
                var atkOffset = (attackStart + framesPerDir + animFrame) >= info.TotalFrameCount ? 0 : framesPerDir;

                return entity.Direction switch
                {
                    Direction.Up    => (attackStart + animFrame, false),
                    Direction.Right => (attackStart + atkOffset + animFrame, false),
                    Direction.Down  => (attackStart + atkOffset + animFrame, true),
                    Direction.Left  => (attackStart + animFrame, true),
                    _               => (info.AttackFrameIndex + animFrame, false)
                };
            }

            default:
                return GetCreatureIdleFrame(entity, in info);
        }
    }

    /// <summary>
    ///     Returns the correct aisling frame index, flip flag, EPF animation suffix, and front-facing flag for the entity's
    ///     current state. IsFrontFacing determines layer draw order (front vs back).
    /// </summary>
    public static (int FrameIndex, bool Flip, string AnimSuffix, bool IsFrontFacing) GetAislingFrame(WorldEntity entity)
    {
        switch (entity.AnimState)
        {
            case EntityAnimState.Walking:
            {
                var isFront = entity.Direction is Direction.Right or Direction.Down;
                var baseFrame = isFront ? AISLING_RIGHT_WALK_BASE : AISLING_UP_WALK_BASE;
                var frameIndex = baseFrame + Math.Clamp(entity.AnimFrameIndex, 0, DEFAULT_WALK_FRAMES - 1);
                var flip = entity.Direction is Direction.Down or Direction.Left;

                return (frameIndex, flip, "01", isFront);
            }

            case EntityAnimState.BodyAnim:
            {
                (var suffix, var framesPerDir, var upStart, var rightStart)
                    = ResolveBodyAnimParams(entity.ActiveBodyAnimation ?? BodyAnimation.Assail);

                var isFront = entity.Direction is Direction.Right or Direction.Down;
                var frameIndex = Math.Clamp(entity.AnimFrameIndex, 0, Math.Max(framesPerDir - 1, 0));
                var dirBase = isFront ? rightStart : upStart;
                var flip = entity.Direction is Direction.Down or Direction.Left;

                return (dirBase + frameIndex, flip, suffix, isFront);
            }

            default:
            {
                var isFront = entity.Direction is Direction.Right or Direction.Down;
                var flip = entity.Direction is Direction.Down or Direction.Left;

                if (entity.IdleAnimFrameCount > 0)
                    return (entity.IdleAnimTick % entity.IdleAnimFrameCount, flip, "04", isFront);

                return (isFront ? 5 : 0, flip, "01", isFront);
            }
        }
    }
    #endregion

    #region Helpers
    /// <summary>
    ///     Maps a BodyAnimation enum to its EPF animation suffix, frames-per-direction count, and the starting frame index
    ///     for Up and Right directions within that EPF file. Multiple animations can share a single suffix file.
    ///     Returns ("01", 0, 0, 0) for emotes (no body animation change).
    /// </summary>
    public static (string Suffix, int FramesPerDirection, int UpStart, int RightStart) ResolveBodyAnimParams(BodyAnimation anim)
    {
        if (DataUtilities.IsEmote(anim))

            //blowkiss and wave are emotes with body animations from "03"
            return anim switch
            {
                BodyAnimation.BlowKiss => ("03", 2, 2, 4),
                BodyAnimation.Wave     => ("03", 2, 6, 8),
                _                      => ("01", 0, 0, 0)
            };

        //reference: chaosassetmanager animationdefinitions
        return anim switch
        {
            //02 — assail (2 frames per dir)
            BodyAnimation.Assail => ("02", 2, 0, 2),

            //03 — peasant animations (shared file)
            BodyAnimation.HandsUp => ("03", 1, 0, 1),

            //b — priest/bard
            BodyAnimation.PriestCast => ("b", 3, 0, 3),
            BodyAnimation.PlayNotes  => ("b", 3, 6, 9),

            //c — warrior
            BodyAnimation.TwoHandAtk => ("c", 4, 0, 4),
            BodyAnimation.JumpAttack => ("c", 3, 8, 11),
            BodyAnimation.Swipe      => ("c", 2, 14, 16),
            BodyAnimation.HeavySwipe => ("c", 3, 18, 21),
            BodyAnimation.Jump       => ("c", 3, 24, 27),

            //d — monk
            BodyAnimation.Kick           => ("d", 3, 0, 3),
            BodyAnimation.Punch          => ("d", 2, 6, 8),
            BodyAnimation.RoundHouseKick => ("d", 4, 10, 14),

            //e — rogue
            BodyAnimation.Stab         => ("e", 2, 0, 2),
            BodyAnimation.DoubleStab   => ("e", 2, 4, 6),
            BodyAnimation.BowShot      => ("e", 4, 8, 12),
            BodyAnimation.HeavyBowShot => ("e", 6, 16, 22),
            BodyAnimation.LongBowShot  => ("e", 4, 28, 32),

            //f — wizard
            BodyAnimation.WizardCast => ("f", 2, 0, 2),
            BodyAnimation.Summon     => ("f", 4, 4, 8),

            //handsup2 — same as handsup
            BodyAnimation.HandsUp2 => ("03", 1, 0, 1),

            //default fallback — assail
            _ => ("02", 2, 0, 2)
        };
    }

    private const float DEFAULT_EMOTE_DURATION_MS = 1500f;

    /// <summary>
    ///     Maps an emote BodyAnimation to the starting frame index, frame count, and total display duration in emot01.epf.
    ///     Multi-frame emotes cycle evenly across the display duration. Returns (-1, 0, 0) for non-emotes.
    /// </summary>
    public static (int StartFrame, int FrameCount, float DurationMs) ResolveEmoteFrames(BodyAnimation anim)
        => anim switch
        {
            BodyAnimation.Smile       => (0, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Cry         => (1, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Frown       => (2, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Wink        => (3, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Surprise    => (4, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Tongue      => (5, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Pleasant    => (6, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Snore       => (7, 2, 2000),
            BodyAnimation.Mouth       => (9, 2, 2000),
            BodyAnimation.BlowKiss    => (2, 2, 2000),
            BodyAnimation.Wave        => (6, 2, 1375),
            BodyAnimation.RockOn      => (11, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Peace       => (12, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Stop        => (13, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Ouch        => (14, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Impatient   => (15, 3, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Shock       => (18, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Pleasure    => (19, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Love        => (20, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.SweatDrop   => (21, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Whistle     => (22, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Irritation  => (23, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Silly       => (24, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Cute        => (25, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Yelling     => (26, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Mischievous => (27, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Evil        => (28, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Horror      => (29, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.PuppyDog    => (30, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.StoneFaced  => (31, 1, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Tears       => (32, 3, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.FiredUp     => (35, 3, DEFAULT_EMOTE_DURATION_MS),
            BodyAnimation.Confused    => (38, 4, 2000),
            _                         => (-1, 0, 0)
        };

    private static (int FrameIndex, bool Flip) GetCreatureIdleFrame(WorldEntity entity, in CreatureAnimInfo info)
    {
        int baseIndex;
        int dirOffset;
        int frameCount;

        //must align with UpdateCreatureIdleCycle: StaticNoIdle always holds the first walk frame
        //(even when StandingFrameCount > 0) because no idle loop is defined.
        if (info.OptionalAnimationFrameCount == 0)
        {
            baseIndex = info.WalkFrameIndex;
            dirOffset = info.WalkFrameCount;
            frameCount = 1;
        } else if ((info.StandingFrameCount == 0) || (info.StandingFrameCount == info.OptionalAnimationFrameCount))
        {
            //NormalIdle: one continuous loop that always starts at StandingFrameIndex, even when
            //StandingFrameCount is zero.
            baseIndex = info.StandingFrameIndex;
            frameCount = info.OptionalAnimationFrameCount;
            dirOffset = info.OptionalAnimationFrameCount;
        } else
        {
            //NormalPlusOptional: cycle length depends on which loop was picked this cycle.
            baseIndex = info.StandingFrameIndex;
            frameCount = entity.IdleOptionalActive ? info.OptionalAnimationFrameCount : info.StandingFrameCount;
            dirOffset = info.OptionalAnimationFrameCount;
        }

        //single-direction sprite: second direction frames don't exist — reuse base frames for all directions
        if ((baseIndex + dirOffset) >= info.TotalFrameCount)
            dirOffset = 0;

        //cycle through the standing frames using the entity's idle tick
        var frameTick = frameCount > 1 ? entity.IdleAnimTick % frameCount : 0;

        return entity.Direction switch
        {
            Direction.Up    => (baseIndex + frameTick, false),
            Direction.Right => (baseIndex + dirOffset + frameTick, false),
            Direction.Down  => (baseIndex + dirOffset + frameTick, true),
            Direction.Left  => (baseIndex + frameTick, true),
            _               => (baseIndex + frameTick, false)
        };
    }

    /// <summary>
    ///     Returns the first idle frame index and flip flag for a creature sprite facing the given direction. Does not require
    ///     a WorldEntity — suitable for portraits, previews, and other static rendering.
    /// </summary>
    public static (int FrameIndex, bool Flip) GetCreatureIdleFrame(in CreatureAnimInfo info, Direction direction)
    {
        var baseIndex = info.StandingFrameCount > 0 ? info.StandingFrameIndex : info.WalkFrameIndex;

        var dirOffset = info.StandingFrameCount > 0
            ? info.OptionalAnimationFrameCount > 0 ? info.OptionalAnimationFrameCount : info.StandingFrameCount
            : info.WalkFrameCount;

        if ((baseIndex + dirOffset) >= info.TotalFrameCount)
            dirOffset = 0;

        return direction switch
        {
            Direction.Up    => (baseIndex, false),
            Direction.Right => (baseIndex + dirOffset, false),
            Direction.Down  => (baseIndex + dirOffset, true),
            Direction.Left  => (baseIndex, true),
            _               => (baseIndex, false)
        };
    }

    /// <summary>
    ///     Computes the starting visual offset for a walk animation. The entity has already moved to the new tile; this offset
    ///     places it visually at the old position, then lerps to zero.
    /// </summary>
    private static Vector2 GetWalkOffset(Direction direction)
        => direction switch
        {
            Direction.Up    => new Vector2(-28, 14),
            Direction.Right => new Vector2(-28, -14),
            Direction.Down  => new Vector2(28, -14),
            Direction.Left  => new Vector2(28, 14),
            _               => Vector2.Zero
        };

    /// <summary>
    ///     Returns a pixel-snapped walk offset for the given frame. Uses integer division to distribute the total pixel delta
    ///     evenly across frames, avoiding fractional positions that cause wobble on the isometric grid.
    /// </summary>
    private static Vector2 GetSteppedWalkOffset(Vector2 startOffset, int frameIndex, int frameCount)
    {
        if (frameCount <= 0)
            return Vector2.Zero;

        //compute remaining offset, ensuring x is always even so that y = x/2 is exact.
        //this maintains the 2:1 isometric pixel ratio and prevents 1px wobble.
        var startX = (int)startOffset.X;
        var startY = (int)startOffset.Y;

        //a zero start offset means the direction did not map to a cardinal walk vector
        //(GetWalkOffset returned Vector2.Zero). there is no slope to follow — short-circuit
        //before the y = x * startY / startX division would throw.
        if (startX == 0)
            return Vector2.Zero;

        var framesLeft = frameCount - (frameIndex + 1);

        var x = startX * framesLeft / frameCount;

        //round x to nearest even number (toward zero) to keep the 2:1 ratio exact
        if ((x & 1) != 0)
            x += x > 0 ? -1 : 1;

        var y = x * startY / startX;

        return new Vector2(x, y);
    }

    /// <summary>
    ///     Maps a logical frame index (0..logicalCount-1) to an actual frame index (0..actualCount-1) proportionally. Used
    ///     when the walk animation has a different frame count than the default 5.
    /// </summary>
    private static int MapFrameIndex(int logicalFrame, int logicalCount, int actualCount)
    {
        if ((logicalCount <= 0) || (actualCount <= 0))
            return 0;

        return Math.Clamp(logicalFrame * actualCount / logicalCount, 0, actualCount - 1);
    }
    #endregion
}