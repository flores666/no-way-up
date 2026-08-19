using System;
using System.Collections.Generic;
using Godot;

namespace LineZero.World2D.Lighting;

public enum FlashlightShadowCastingMode
{
	FullOcclusion = 0,
	FiniteHeight = 1
}

/// <summary>
/// Generates flashlight shadow geometry from collision shapes.
/// Tall blockers such as walls use real LightOccluder2D edges. Low props can use
/// finite projected shadows so a 20-50 cm item does not cast an infinitely long
/// 2D shadow like a full-height wall.
/// </summary>
public sealed partial class CollisionLightOccluderGenerator2D : Node2D
{
	private static readonly StringName FlashlightShadowSourceGroup =
		new("player_flashlight_shadow_source");

	private const float DirectionEpsilonSquared = 0.0001f;

	private readonly List<RectangleOccluderSet> _rectangleOccluders = new();
	private readonly List<CurvedOccluderSet> _curvedOccluders = new();
	private readonly List<FiniteShadowSet> _finiteShadows = new();
	private Node2D? _flashlightShadowSource;

	[Export]
	public int OccluderLightMask { get; set; } = 1;

	[Export]
	public FlashlightShadowCastingMode ShadowCastingMode { get; set; } =
		FlashlightShadowCastingMode.FullOcclusion;

	[Export(PropertyHint.Range, "0.05,3.0,0.05,or_greater")]
	public float ObjectHeightMeters { get; set; } = 0.5f;

	[Export(PropertyHint.Range, "0.5,3.0,0.05,or_greater")]
	public float ReferenceCharacterHeightMeters { get; set; } = 1.8f;

	[Export(PropertyHint.Range, "8,96,1,or_greater")]
	public float ReferenceCharacterShadowLengthPixels { get; set; } = 36.0f;

	[Export(PropertyHint.Range, "1,24,0.5,or_greater")]
	public float MinimumFiniteShadowLengthPixels { get; set; } = 2.0f;

	[Export(PropertyHint.Range, "0,1,0.01")]
	public float FiniteShadowOpacity { get; set; } = 0.18f;

	[Export(PropertyHint.Range, "10,70,1")]
	public float FlashlightHalfAngleDegrees { get; set; } = 42.0f;

	public override void _Ready()
	{
		if (GetParent() is not CollisionObject2D collisionOwner)
		{
			throw new InvalidOperationException(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' requires a " +
				$"{nameof(CollisionObject2D)} parent.");
		}

		ValidateConfiguration();

		foreach (Node child in collisionOwner.GetChildren())
		{
			if (child is not CollisionShape2D
				{ Disabled: false, Shape: not null } collisionShape)
			{
				continue;
			}

			if (ShadowCastingMode == FlashlightShadowCastingMode.FiniteHeight)
			{
				CreateFiniteShadow(collisionShape);
				continue;
			}

			switch (collisionShape.Shape)
			{
				case RectangleShape2D rectangle:
					CreateRectangleOccluders(collisionShape, rectangle.Size);
					break;

				case CircleShape2D circle:
					CreateCurvedOccluder(collisionShape, circle.Radius, straightHalfHeight: 0.0f);
					break;

				case CapsuleShape2D capsule:
					CreateCapsuleOccluder(collisionShape, capsule);
					break;

				default:
					WarnUnsupportedShape(collisionShape);
					break;
			}
		}

		TryResolveFlashlightShadowSource();
		UpdateShadowPresentation();
		SetProcess(
			_rectangleOccluders.Count > 0 ||
			_curvedOccluders.Count > 0 ||
			_finiteShadows.Count > 0);
	}

	public override void _Process(double delta)
	{
		_ = delta;

		if (!IsValidShadowSource())
		{
			TryResolveFlashlightShadowSource();
		}

		UpdateShadowPresentation();
	}

	private void ValidateConfiguration()
	{
		if (OccluderLightMask <= 0)
		{
			throw new InvalidOperationException(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' requires a positive " +
				"occluder light mask.");
		}

		if (!IsFinitePositive(ObjectHeightMeters) ||
			!IsFinitePositive(ReferenceCharacterHeightMeters) ||
			!IsFinitePositive(ReferenceCharacterShadowLengthPixels) ||
			!IsFinitePositive(MinimumFiniteShadowLengthPixels) ||
			!float.IsFinite(FiniteShadowOpacity) ||
			FiniteShadowOpacity < 0.0f ||
			FiniteShadowOpacity > 1.0f ||
			!float.IsFinite(FlashlightHalfAngleDegrees) ||
			FlashlightHalfAngleDegrees <= 0.0f ||
			FlashlightHalfAngleDegrees >= 90.0f)
		{
			throw new InvalidOperationException(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' has invalid shadow settings.");
		}
	}

	private void CreateFiniteShadow(CollisionShape2D collisionShape)
	{
		FiniteFootprint footprint = collisionShape.Shape switch
		{
			RectangleShape2D rectangle when IsFinitePositive(rectangle.Size.X) &&
											  IsFinitePositive(rectangle.Size.Y) =>
				FiniteFootprint.ForRectangle(rectangle.Size * 0.5f),

			CircleShape2D circle when IsFinitePositive(circle.Radius) =>
				FiniteFootprint.ForCircle(circle.Radius),

			CapsuleShape2D capsule when IsValidCapsule(capsule) =>
				FiniteFootprint.ForCapsule(
					capsule.Radius,
					Mathf.Max(0.0f, capsule.Height * 0.5f - capsule.Radius)),

			_ => default
		};

		if (!footprint.IsValid)
		{
			WarnUnsupportedShape(collisionShape);
			return;
		}

		float heightRatio = Mathf.Clamp(
			ObjectHeightMeters / ReferenceCharacterHeightMeters,
			0.0f,
			1.0f);
		float opacityScale = Mathf.Lerp(0.45f, 1.0f, heightRatio);

		Polygon2D softShadow = CreateFiniteShadowPolygon(
			$"{collisionShape.Name}FlashlightPenumbra",
			alphaMultiplier: 0.30f * opacityScale,
			zIndex: -3);
		Polygon2D coreShadow = CreateFiniteShadowPolygon(
			$"{collisionShape.Name}FlashlightShadow",
			alphaMultiplier: opacityScale,
			zIndex: -2);

		_finiteShadows.Add(new FiniteShadowSet(
			collisionShape,
			footprint,
			softShadow,
			coreShadow));
	}

	private Polygon2D CreateFiniteShadowPolygon(
		string name,
		float alphaMultiplier,
		int zIndex)
	{
		Polygon2D polygon = new()
		{
			Name = name,
			Polygon = [Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero],
			Color = new Color(0.0f, 0.0f, 0.0f, FiniteShadowOpacity * alphaMultiplier),
			ZIndex = zIndex,
			LightMask = 0,
			Visible = false
		};

		AddChild(polygon);
		return polygon;
	}

	private void CreateRectangleOccluders(CollisionShape2D collisionShape, Vector2 size)
	{
		if (!IsFinitePositive(size.X) || !IsFinitePositive(size.Y))
		{
			GD.PushWarning(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' skipped invalid " +
				$"rectangle '{collisionShape.Name}' with size {size}.");
			return;
		}

		Vector2 half = size * 0.5f;
		RectangleEdgeOccluder left = CreateEdgeOccluder(collisionShape, "Left");
		RectangleEdgeOccluder right = CreateEdgeOccluder(collisionShape, "Right");
		RectangleEdgeOccluder top = CreateEdgeOccluder(collisionShape, "Top");
		RectangleEdgeOccluder bottom = CreateEdgeOccluder(collisionShape, "Bottom");

		_rectangleOccluders.Add(new RectangleOccluderSet(
			collisionShape,
			half,
			left,
			right,
			top,
			bottom));
	}

	private void CreateCapsuleOccluder(
		CollisionShape2D collisionShape,
		CapsuleShape2D capsule)
	{
		if (!IsValidCapsule(capsule))
		{
			GD.PushWarning(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' skipped invalid " +
				$"capsule '{collisionShape.Name}'.");
			return;
		}

		float straightHalfHeight = Mathf.Max(0.0f, capsule.Height * 0.5f - capsule.Radius);
		CreateCurvedOccluder(collisionShape, capsule.Radius, straightHalfHeight);
	}

	private void CreateCurvedOccluder(
		CollisionShape2D collisionShape,
		float radius,
		float straightHalfHeight)
	{
		if (!IsFinitePositive(radius) ||
			!float.IsFinite(straightHalfHeight) ||
			straightHalfHeight < 0.0f)
		{
			GD.PushWarning(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' skipped invalid " +
				$"curved shape '{collisionShape.Name}'.");
			return;
		}

		OccluderPolygon2D polygon = new()
		{
			Polygon = [Vector2.Zero, Vector2.Zero],
			Closed = false
		};

		LightOccluder2D occluder = new()
		{
			Name = $"{collisionShape.Name}FarSideLightOccluder",
			Transform = collisionShape.Transform,
			Occluder = polygon,
			OccluderLightMask = OccluderLightMask,
			Visible = false
		};

		AddChild(occluder);
		_curvedOccluders.Add(new CurvedOccluderSet(
			collisionShape,
			polygon,
			occluder,
			radius,
			straightHalfHeight));
	}

	private RectangleEdgeOccluder CreateEdgeOccluder(
		CollisionShape2D collisionShape,
		string edgeName)
	{
		OccluderPolygon2D occluderPolygon = new()
		{
			Polygon = [Vector2.Zero, Vector2.Zero],
			Closed = false
		};

		LightOccluder2D occluder = new()
		{
			Name = $"{collisionShape.Name}{edgeName}LightOccluder",
			Transform = collisionShape.Transform,
			Occluder = occluderPolygon,
			OccluderLightMask = OccluderLightMask,
			Visible = false
		};

		AddChild(occluder);
		return new RectangleEdgeOccluder(occluderPolygon, occluder);
	}

	private void UpdateShadowPresentation()
	{
		if (!IsValidShadowSource())
		{
			SetAllShadowPresentationVisible(false);
			return;
		}

		Vector2 lightGlobalPosition = _flashlightShadowSource!.GlobalPosition;
		UpdateRectangleOccluders(lightGlobalPosition);
		UpdateCurvedOccluders(lightGlobalPosition);
		UpdateFiniteShadows(lightGlobalPosition);
	}

	private void UpdateRectangleOccluders(Vector2 lightGlobalPosition)
	{
		foreach (RectangleOccluderSet set in _rectangleOccluders)
		{
			if (!GodotObject.IsInstanceValid(set.CollisionShape) || set.CollisionShape.Disabled)
			{
				set.SetAllVisible(false);
				continue;
			}

			Vector2 localLight = set.CollisionShape.ToLocal(lightGlobalPosition);
			if (!IsFinite(localLight) ||
				localLight.LengthSquared() <= DirectionEpsilonSquared)
			{
				set.SetAllVisible(false);
				continue;
			}

			// A rectangular wall uses the far silhouette chain rather than four binary
			// on/off edges. The dominant far edge stays full-size while the adjacent
			// far edge grows continuously from zero to full length as the light angle
			// approaches a diagonal. This avoids the visible shadow pop that occurred
			// when an entire wall edge was toggled at once.
			set.UpdateContinuousFarEdges(localLight);
		}
	}

	private void UpdateCurvedOccluders(Vector2 lightGlobalPosition)
	{
		foreach (CurvedOccluderSet set in _curvedOccluders)
		{
			if (!GodotObject.IsInstanceValid(set.CollisionShape) || set.CollisionShape.Disabled)
			{
				set.Occluder.Visible = false;
				continue;
			}

			Vector2 localLight = set.CollisionShape.ToLocal(lightGlobalPosition);
			if (!IsFinite(localLight) ||
				localLight.LengthSquared() <= DirectionEpsilonSquared)
			{
				set.Occluder.Visible = false;
				continue;
			}

			Vector2 towardLight = localLight.Normalized();
			Vector2 awayFromLight = -towardLight;
			Vector2 tangent = new(-towardLight.Y, towardLight.X);

			float farExtent =
				set.Radius + Mathf.Abs(awayFromLight.Y) * set.StraightHalfHeight;
			float tangentExtent =
				set.Radius + Mathf.Abs(tangent.Y) * set.StraightHalfHeight;

			Vector2 farCenter = awayFromLight * farExtent;
			set.Polygon.Polygon =
			[
				farCenter - tangent * tangentExtent,
				farCenter + tangent * tangentExtent
			];
			set.Occluder.Visible = true;
		}
	}

	private void UpdateFiniteShadows(Vector2 lightGlobalPosition)
	{
		if (_finiteShadows.Count == 0)
		{
			return;
		}

		if (_flashlightShadowSource is not PointLight2D pointLight ||
			!pointLight.Enabled ||
			!pointLight.IsVisibleInTree())
		{
			SetFiniteShadowsVisible(false);
			return;
		}

		Vector2 beamDirection = pointLight.GlobalTransform.X.Normalized();
		float coneCos = Mathf.Cos(Mathf.DegToRad(FlashlightHalfAngleDegrees));
		float maxDistance = pointLight.Texture is null
			? float.PositiveInfinity
			: pointLight.Texture.GetSize().X * 0.5f * pointLight.TextureScale;

		float heightRatio = ObjectHeightMeters / ReferenceCharacterHeightMeters;
		float shadowLength = Mathf.Max(
			MinimumFiniteShadowLengthPixels,
			ReferenceCharacterShadowLengthPixels * heightRatio);
		float footprintScale = Mathf.Clamp(0.25f + heightRatio * 0.75f, 0.30f, 1.0f);

		foreach (FiniteShadowSet set in _finiteShadows)
		{
			if (!GodotObject.IsInstanceValid(set.CollisionShape) || set.CollisionShape.Disabled)
			{
				set.SetVisible(false);
				continue;
			}

			Vector2 shapeCenterGlobal = set.CollisionShape.GlobalPosition;
			Vector2 toObject = shapeCenterGlobal - lightGlobalPosition;
			float distance = toObject.Length();
			if (!float.IsFinite(distance) ||
				distance <= 1.0f ||
				distance > maxDistance)
			{
				set.SetVisible(false);
				continue;
			}

			Vector2 toObjectDirection = toObject / distance;
			if (beamDirection.Dot(toObjectDirection) < coneCos)
			{
				set.SetVisible(false);
				continue;
			}

			Vector2 localLight = set.CollisionShape.ToLocal(lightGlobalPosition);
			if (!IsFinite(localLight) ||
				localLight.LengthSquared() <= DirectionEpsilonSquared)
			{
				set.SetVisible(false);
				continue;
			}

			Vector2 towardLight = localLight.Normalized();
			Vector2 awayFromLight = -towardLight;
			Vector2 tangent = new(-towardLight.Y, towardLight.X);

			set.Footprint.ResolveSupport(
				awayFromLight,
				tangent,
				out float farExtent,
				out float tangentExtent);

			Vector2 farCenter = awayFromLight * farExtent;

			// Low props look better with a compact contact shadow wedge instead of
			// a wide footprint-based strip. The base width stays small and the cone
			// opens gradually with distance so medkits, lockers and similar objects
			// cast short believable shadows instead of detached bars.
			float baseHalfWidth = Mathf.Clamp(
				tangentExtent * Mathf.Lerp(0.16f, 0.38f, heightRatio),
				0.65f,
				Mathf.Lerp(2.4f, 7.5f, heightRatio));
			float farHalfWidth = baseHalfWidth + Mathf.Lerp(1.2f, 4.0f, heightRatio);
			float softBaseHalfWidth = baseHalfWidth + Mathf.Lerp(0.8f, 2.0f, heightRatio);
			float softFarHalfWidth = farHalfWidth + Mathf.Lerp(1.2f, 3.2f, heightRatio);
			Vector2 nearCenter = farCenter + awayFromLight * 0.35f;

			SetFiniteShadowWedge(
				set.CoreShadow,
				set.CollisionShape,
				nearCenter,
				awayFromLight,
				tangent,
				shadowLength,
				baseHalfWidth,
				farHalfWidth);

			SetFiniteShadowWedge(
				set.SoftShadow,
				set.CollisionShape,
				nearCenter + awayFromLight * 0.4f,
				awayFromLight,
				tangent,
				shadowLength + 2.0f,
				softBaseHalfWidth,
				softFarHalfWidth);

			set.SetVisible(true);
		}
	}

	private void SetFiniteShadowWedge(
		Polygon2D target,
		CollisionShape2D collisionShape,
		Vector2 nearCenter,
		Vector2 awayFromLight,
		Vector2 tangent,
		float shadowLength,
		float nearHalfWidth,
		float farHalfWidth)
	{
		Vector2 nearLeft = nearCenter - tangent * nearHalfWidth;
		Vector2 nearRight = nearCenter + tangent * nearHalfWidth;
		Vector2 farCenter = nearCenter + awayFromLight * shadowLength;
		Vector2 farLeft = farCenter - tangent * farHalfWidth;
		Vector2 farRight = farCenter + tangent * farHalfWidth;

		target.Polygon =
		[
			ToLocal(collisionShape.ToGlobal(nearLeft)),
			ToLocal(collisionShape.ToGlobal(nearRight)),
			ToLocal(collisionShape.ToGlobal(farRight)),
			ToLocal(collisionShape.ToGlobal(farLeft))
		];
	}

	private void SetAllShadowPresentationVisible(bool visible)
	{
		foreach (RectangleOccluderSet set in _rectangleOccluders)
		{
			set.SetAllVisible(visible);
		}

		foreach (CurvedOccluderSet set in _curvedOccluders)
		{
			set.Occluder.Visible = visible;
		}

		SetFiniteShadowsVisible(visible);
	}

	private void SetFiniteShadowsVisible(bool visible)
	{
		foreach (FiniteShadowSet set in _finiteShadows)
		{
			set.SetVisible(visible);
		}
	}

	private void TryResolveFlashlightShadowSource()
	{
		Node? source = GetTree().GetFirstNodeInGroup(FlashlightShadowSourceGroup);
		_flashlightShadowSource = source as Node2D;
	}

	private bool IsValidShadowSource()
	{
		return _flashlightShadowSource is not null &&
			   GodotObject.IsInstanceValid(_flashlightShadowSource) &&
			   _flashlightShadowSource.IsInsideTree();
	}

	private void WarnUnsupportedShape(CollisionShape2D collisionShape)
	{
		GD.PushWarning(
			$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' skipped unsupported " +
			$"shape '{collisionShape.Shape?.GetType().Name ?? "null"}' from '{collisionShape.Name}'.");
	}

	private static bool IsValidCapsule(CapsuleShape2D capsule)
	{
		return IsFinitePositive(capsule.Radius) &&
			   IsFinitePositive(capsule.Height) &&
			   capsule.Height >= capsule.Radius * 2.0f;
	}

	private static bool IsFinitePositive(float value)
	{
		return float.IsFinite(value) && value > 0.0f;
	}

	private static bool IsFinite(Vector2 value)
	{
		return float.IsFinite(value.X) && float.IsFinite(value.Y);
	}

	private sealed class RectangleEdgeOccluder
	{
		public RectangleEdgeOccluder(
			OccluderPolygon2D polygon,
			LightOccluder2D occluder)
		{
			Polygon = polygon;
			Occluder = occluder;
		}

		public OccluderPolygon2D Polygon { get; }
		public LightOccluder2D Occluder { get; }

		public void SetSegment(Vector2 from, Vector2 to)
		{
			if ((to - from).LengthSquared() <= DirectionEpsilonSquared)
			{
				Occluder.Visible = false;
				return;
			}

			Polygon.Polygon = [from, to];
			Occluder.Visible = true;
		}

		public void Hide()
		{
			Occluder.Visible = false;
		}
	}

	private sealed class RectangleOccluderSet
	{
		private readonly RectangleEdgeOccluder _left;
		private readonly RectangleEdgeOccluder _right;
		private readonly RectangleEdgeOccluder _top;
		private readonly RectangleEdgeOccluder _bottom;

		public RectangleOccluderSet(
			CollisionShape2D collisionShape,
			Vector2 halfSize,
			RectangleEdgeOccluder left,
			RectangleEdgeOccluder right,
			RectangleEdgeOccluder top,
			RectangleEdgeOccluder bottom)
		{
			CollisionShape = collisionShape;
			HalfSize = halfSize;
			_left = left;
			_right = right;
			_top = top;
			_bottom = bottom;
		}

		public CollisionShape2D CollisionShape { get; }
		public Vector2 HalfSize { get; }

		public void UpdateContinuousFarEdges(Vector2 localLight)
		{
			SetAllVisible(false);

			float absX = Mathf.Abs(localLight.X);
			float absY = Mathf.Abs(localLight.Y);
			if (absX <= 0.0001f && absY <= 0.0001f)
			{
				return;
			}

			Vector2 topLeft = new(-HalfSize.X, -HalfSize.Y);
			Vector2 topRight = new(HalfSize.X, -HalfSize.Y);
			Vector2 bottomRight = new(HalfSize.X, HalfSize.Y);
			Vector2 bottomLeft = new(-HalfSize.X, HalfSize.Y);

			bool lightRight = localLight.X >= 0.0f;
			bool lightBelow = localLight.Y >= 0.0f;

			if (absX >= absY)
			{
				float secondaryRatio = absX <= 0.0001f
					? 0.0f
					: Mathf.Clamp(absY / absX, 0.0f, 1.0f);

				if (lightRight)
				{
					_left.SetSegment(topLeft, bottomLeft);
					if (lightBelow)
					{
						_top.SetSegment(topLeft, topLeft.Lerp(topRight, secondaryRatio));
					}
					else
					{
						_bottom.SetSegment(bottomLeft, bottomLeft.Lerp(bottomRight, secondaryRatio));
					}
				}
				else
				{
					_right.SetSegment(bottomRight, topRight);
					if (lightBelow)
					{
						_top.SetSegment(topRight, topRight.Lerp(topLeft, secondaryRatio));
					}
					else
					{
						_bottom.SetSegment(bottomRight, bottomRight.Lerp(bottomLeft, secondaryRatio));
					}
				}

				return;
			}

			float verticalSecondaryRatio = absY <= 0.0001f
				? 0.0f
				: Mathf.Clamp(absX / absY, 0.0f, 1.0f);

			if (lightBelow)
			{
				_top.SetSegment(topRight, topLeft);
				if (lightRight)
				{
					_left.SetSegment(topLeft, topLeft.Lerp(bottomLeft, verticalSecondaryRatio));
				}
				else
				{
					_right.SetSegment(topRight, topRight.Lerp(bottomRight, verticalSecondaryRatio));
				}
			}
			else
			{
				_bottom.SetSegment(bottomLeft, bottomRight);
				if (lightRight)
				{
					_left.SetSegment(bottomLeft, bottomLeft.Lerp(topLeft, verticalSecondaryRatio));
				}
				else
				{
					_right.SetSegment(bottomRight, bottomRight.Lerp(topRight, verticalSecondaryRatio));
				}
			}
		}

		public void SetAllVisible(bool visible)
		{
			if (visible)
			{
				// Full visibility without a light direction is undefined. Keep the
				// current segments instead of inventing geometry.
				return;
			}

			_left.Hide();
			_right.Hide();
			_top.Hide();
			_bottom.Hide();
		}
	}

	private sealed class CurvedOccluderSet
	{
		public CurvedOccluderSet(
			CollisionShape2D collisionShape,
			OccluderPolygon2D polygon,
			LightOccluder2D occluder,
			float radius,
			float straightHalfHeight)
		{
			CollisionShape = collisionShape;
			Polygon = polygon;
			Occluder = occluder;
			Radius = radius;
			StraightHalfHeight = straightHalfHeight;
		}

		public CollisionShape2D CollisionShape { get; }
		public OccluderPolygon2D Polygon { get; }
		public LightOccluder2D Occluder { get; }
		public float Radius { get; }
		public float StraightHalfHeight { get; }
	}

	private sealed class FiniteShadowSet
	{
		public FiniteShadowSet(
			CollisionShape2D collisionShape,
			FiniteFootprint footprint,
			Polygon2D softShadow,
			Polygon2D coreShadow)
		{
			CollisionShape = collisionShape;
			Footprint = footprint;
			SoftShadow = softShadow;
			CoreShadow = coreShadow;
		}

		public CollisionShape2D CollisionShape { get; }
		public FiniteFootprint Footprint { get; }
		public Polygon2D SoftShadow { get; }
		public Polygon2D CoreShadow { get; }

		public void SetVisible(bool visible)
		{
			SoftShadow.Visible = visible;
			CoreShadow.Visible = visible;
		}
	}

	private readonly struct FiniteFootprint
	{
		private enum Kind
		{
			Invalid = 0,
			Rectangle = 1,
			Circle = 2,
			Capsule = 3
		}

		private readonly Kind _kind;
		private readonly Vector2 _halfSize;
		private readonly float _radius;
		private readonly float _straightHalfHeight;

		private FiniteFootprint(
			Kind kind,
			Vector2 halfSize,
			float radius,
			float straightHalfHeight)
		{
			_kind = kind;
			_halfSize = halfSize;
			_radius = radius;
			_straightHalfHeight = straightHalfHeight;
		}

		public bool IsValid => _kind != Kind.Invalid;

		public static FiniteFootprint ForRectangle(Vector2 halfSize) =>
			new(Kind.Rectangle, halfSize, 0.0f, 0.0f);

		public static FiniteFootprint ForCircle(float radius) =>
			new(Kind.Circle, Vector2.Zero, radius, 0.0f);

		public static FiniteFootprint ForCapsule(float radius, float straightHalfHeight) =>
			new(Kind.Capsule, Vector2.Zero, radius, straightHalfHeight);

		public void ResolveSupport(
			Vector2 awayFromLight,
			Vector2 tangent,
			out float farExtent,
			out float tangentExtent)
		{
			switch (_kind)
			{
				case Kind.Rectangle:
					farExtent =
						Mathf.Abs(awayFromLight.X) * _halfSize.X +
						Mathf.Abs(awayFromLight.Y) * _halfSize.Y;
					tangentExtent =
						Mathf.Abs(tangent.X) * _halfSize.X +
						Mathf.Abs(tangent.Y) * _halfSize.Y;
					return;

				case Kind.Circle:
					farExtent = _radius;
					tangentExtent = _radius;
					return;

				case Kind.Capsule:
					farExtent =
						_radius + Mathf.Abs(awayFromLight.Y) * _straightHalfHeight;
					tangentExtent =
						_radius + Mathf.Abs(tangent.Y) * _straightHalfHeight;
					return;

				default:
					farExtent = 0.0f;
					tangentExtent = 0.0f;
					return;
			}
		}
	}
}
