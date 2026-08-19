using System;
using Godot;

namespace LineZero.World2D.Lighting;

/// <summary>
/// Mirrors supported collision shapes into LightOccluder2D nodes so the same
/// geometry that blocks movement also blocks dynamic 2D lights.
/// </summary>
public sealed partial class CollisionLightOccluderGenerator2D : Node2D
{
	private const int CircleSegments = 20;
	private const int CapsuleArcSegments = 10;

	[Export]
	public int OccluderLightMask { get; set; } = 1;

	[Export(PropertyHint.Range, "0.0,16.0,0.5")]
	public float SurfaceReceiverThickness { get; set; } = 6.0f;

	public override void _Ready()
	{
		if (GetParent() is not CollisionObject2D collisionOwner)
		{
			throw new InvalidOperationException(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' requires a " +
				$"{nameof(CollisionObject2D)} parent.");
		}

		if (OccluderLightMask <= 0)
		{
			throw new InvalidOperationException(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' requires a positive " +
				"occluder light mask.");
		}

		if (!float.IsFinite(SurfaceReceiverThickness) || SurfaceReceiverThickness < 0.0f)
		{
			throw new InvalidOperationException(
				$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' requires a non-negative " +
				"finite surface receiver thickness.");
		}

		foreach (Node child in collisionOwner.GetChildren())
		{
			if (child is not CollisionShape2D
				{ Disabled: false, Shape: not null } collisionShape)
			{
				continue;
			}

			Vector2[]? polygon = CreatePolygon(collisionShape.Shape, SurfaceReceiverThickness);
			if (polygon is null || polygon.Length < 3)
			{
				GD.PushWarning(
					$"{nameof(CollisionLightOccluderGenerator2D)} on '{Name}' skipped unsupported " +
					$"shape '{collisionShape.Shape.GetType().Name}' from '{collisionShape.Name}'.");
				continue;
			}

			OccluderPolygon2D occluderPolygon = new()
			{
				Polygon = polygon
			};

			LightOccluder2D occluder = new()
			{
				Name = $"{collisionShape.Name}LightOccluder",
				Transform = collisionShape.Transform,
				Occluder = occluderPolygon,
				OccluderLightMask = OccluderLightMask
			};

			AddChild(occluder);
		}
	}

	private static Vector2[]? CreatePolygon(Shape2D shape, float surfaceReceiverThickness)
	{
		return shape switch
		{
			RectangleShape2D rectangle =>
				CreateRectanglePolygon(rectangle.Size, surfaceReceiverThickness),
			CircleShape2D circle =>
				CreateCirclePolygon(circle.Radius, surfaceReceiverThickness),
			CapsuleShape2D capsule =>
				CreateCapsulePolygon(capsule.Radius, capsule.Height, surfaceReceiverThickness),
			_ => null
		};
	}

	private static Vector2[] CreateRectanglePolygon(Vector2 size, float inset)
	{
		Vector2 insetSize = size - Vector2.One * (inset * 2.0f);
		if (!float.IsFinite(insetSize.X) || !float.IsFinite(insetSize.Y) ||
			insetSize.X <= 0.0f || insetSize.Y <= 0.0f)
		{
			return Array.Empty<Vector2>();
		}

		Vector2 half = insetSize * 0.5f;
		return
		[
			new Vector2(-half.X, -half.Y),
			new Vector2(half.X, -half.Y),
			new Vector2(half.X, half.Y),
			new Vector2(-half.X, half.Y)
		];
	}

	private static Vector2[] CreateCirclePolygon(float radius, float inset)
	{
		radius -= inset;
		if (!float.IsFinite(radius) || radius <= 0.0f)
		{
			return Array.Empty<Vector2>();
		}

		Vector2[] points = new Vector2[CircleSegments];
		for (int index = 0; index < points.Length; index++)
		{
			float angle = Mathf.Tau * index / points.Length;
			points[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}

		return points;
	}

	private static Vector2[] CreateCapsulePolygon(
		float radius,
		float height,
		float inset)
	{
		radius -= inset;
		height -= inset * 2.0f;

		if (!float.IsFinite(radius) || !float.IsFinite(height) ||
			radius <= 0.0f || height <= 0.0f || height < radius * 2.0f)
		{
			return Array.Empty<Vector2>();
		}

		float straightHalfHeight = Mathf.Max(0.0f, height * 0.5f - radius);
		Vector2[] points = new Vector2[(CapsuleArcSegments + 1) * 2];
		int pointIndex = 0;

		for (int index = 0; index <= CapsuleArcSegments; index++)
		{
			float angle = Mathf.Lerp(0.0f, Mathf.Pi, index / (float)CapsuleArcSegments);
			points[pointIndex++] = new Vector2(
				Mathf.Cos(angle) * radius,
				-straightHalfHeight - Mathf.Sin(angle) * radius);
		}

		for (int index = 0; index <= CapsuleArcSegments; index++)
		{
			float angle = Mathf.Lerp(Mathf.Pi, Mathf.Tau, index / (float)CapsuleArcSegments);
			points[pointIndex++] = new Vector2(
				Mathf.Cos(angle) * radius,
				straightHalfHeight - Mathf.Sin(angle) * radius);
		}

		return points;
	}
}
