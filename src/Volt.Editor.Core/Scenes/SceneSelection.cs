namespace Volt.Editor.Core.Scenes;

/// <summary>
/// Helpers for reading a multi-selection of scene objects.
/// </summary>
public static class SceneSelection
{
	/// <summary>
	/// Narrows a selection to the objects that are not inside another
	/// selected object.
	/// </summary>
	/// <remarks>
	/// A transform applied to a parent already carries its children, so
	/// transforming both would move the child twice. Selection,
	/// highlighting, and counting still use the full selection; only
	/// transform edits use these roots.
	/// </remarks>
	/// <param name="scene">The scene the ids belong to.</param>
	/// <param name="entities">The selected entity ids.</param>
	/// <returns>
	/// The selected objects with no selected ancestor, in the order the
	/// ids were given.
	/// </returns>
	public static IReadOnlyList<SceneEntity> Roots(
		SceneDocument scene,
		IEnumerable<Guid> entities)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(entities);

		List<Guid> ids = [.. entities];
		HashSet<Guid> selected = [.. ids];
		List<SceneEntity> roots = [];

		foreach (Guid id in ids)
		{
			if (scene.Find(id) is not SceneEntity entity)
			{
				continue;
			}

			if (!HasSelectedAncestor(scene, entity, selected))
			{
				roots.Add(entity);
			}
		}

		return roots;
	}

	private static bool HasSelectedAncestor(
		SceneDocument scene,
		SceneEntity entity,
		HashSet<Guid> selected)
	{
		HashSet<Guid> visited = [];
		Guid? parentId = entity.ParentId;

		while (parentId is Guid id && visited.Add(id))
		{
			if (selected.Contains(id))
			{
				return true;
			}

			parentId = scene.Find(id)?.ParentId;
		}

		return false;
	}
}
