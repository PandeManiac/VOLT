using Volt.Editor.Core.Projects;
using Volt.Editor.Core.Scenes;
using Volt.Ui;
using Volt.Ui.Rendering;
using Volt.Ui.Styling;

namespace Volt.Editor;

/// <summary>
/// Maps editor data to the icon and tint that represent it.
/// </summary>
/// <remarks>
/// One table keeps the hierarchy, project browser, inspector, and
/// menus describing the same kind of thing the same way. It lives in
/// the editor rather than Volt.Ui because asset kinds and authored
/// component types are engine concepts.
/// </remarks>
internal static class EditorIcons
{
	public static UiIcon ForAsset(ProjectAssetKind kind)
	{
		return kind switch
		{
			ProjectAssetKind.Scene => UiIcon.Scene,
			ProjectAssetKind.Mesh => UiIcon.Mesh,
			ProjectAssetKind.Material => UiIcon.Material,
			ProjectAssetKind.Texture => UiIcon.Texture,
			ProjectAssetKind.Script => UiIcon.Script,
			ProjectAssetKind.Font => UiIcon.Script,
			_ => UiIcon.Dot
		};
	}

	public static UiColor AssetColor(ProjectAssetKind kind, UiPalette palette)
	{
		return kind switch
		{
			ProjectAssetKind.Scene => palette.Accent,
			ProjectAssetKind.Mesh => palette.Success,
			ProjectAssetKind.Material => palette.Warning,
			ProjectAssetKind.Texture => UiColor.FromRgb(0xB08CFF),
			ProjectAssetKind.Script => UiColor.FromRgb(0x8CBF7A),
			ProjectAssetKind.Font => palette.TextMuted,
			_ => palette.TextMuted
		};
	}

	/// <summary>
	/// Gets the row icon for an entity: its renderer, its camera, or a
	/// plain transform-only object.
	/// </summary>
	public static UiIcon ForEntity(SceneEntity entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		if (entity.GetComponent<CameraData>() is not null)
		{
			return UiIcon.Camera;
		}

		if (entity.GetComponent<MeshRendererData>() is MeshRendererData renderer)
		{
			return renderer.Mesh switch
			{
				"builtin:sphere" => UiIcon.Sphere,
				"builtin:plane" or "builtin:grid" => UiIcon.Plane,
				"builtin:cube" => UiIcon.Cube,
				_ => UiIcon.Mesh
			};
		}

		return entity.Components.Count > 0
			? UiIcon.Layers
			: UiIcon.Local;
	}

	public static UiColor EntityColor(SceneEntity entity, UiPalette palette)
	{
		ArgumentNullException.ThrowIfNull(entity);

		if (entity.GetComponent<CameraData>() is not null)
		{
			return palette.Info;
		}

		if (entity.GetComponent<MeshRendererData>() is not null)
		{
			return palette.Success;
		}

		return palette.TextMuted;
	}

	public static UiIcon ForComponent(string typeId)
	{
		return typeId switch
		{
			CameraData.ComponentTypeId => UiIcon.Camera,
			MeshRendererData.ComponentTypeId => UiIcon.Cube,
			SpinBehaviourData.ComponentTypeId => UiIcon.Refresh,
			MoveBehaviourData.ComponentTypeId => UiIcon.Move,
			_ => UiIcon.Layers
		};
	}

	public static UiIcon ForLogLevel(EditorLogLevel level)
	{
		return level switch
		{
			EditorLogLevel.Error => UiIcon.Error,
			EditorLogLevel.Warning => UiIcon.Warning,
			_ => UiIcon.Info
		};
	}

	public static UiColor LogColor(EditorLogLevel level, UiPalette palette)
	{
		return level switch
		{
			EditorLogLevel.Error => palette.Danger,
			EditorLogLevel.Warning => palette.Warning,
			_ => palette.Info
		};
	}
}
