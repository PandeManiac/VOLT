namespace Volt.Editor.Core.Scenes;

/// <summary>
/// A snapshot of scene objects, copied so it survives the source
/// scene closing, changing, or losing the objects it came from.
/// </summary>
/// <remarks>
/// <para>
/// A capture keeps the selected objects and everything under them.
/// Selected objects nested inside another selected object are left
/// to their parent, exactly as duplication and transform edits do,
/// so copying a parent never produces a second partial copy of its
/// children.
/// </para>
/// <para>
/// A paste creates new identities rather than reusing the captured
/// ones. That is what makes pasting into the same scene, a reloaded
/// scene, or a different scene work without the snapshot colliding
/// with anything already there. Pasted roots are placed at the top
/// of the hierarchy; the parent links inside the captured subtree
/// are preserved.
/// </para>
/// </remarks>
public sealed class EntityClipboard
{
	private readonly List<ClipboardEntry> _entries = [];

	/// <summary>
	/// Gets whether the clipboard holds no objects.
	/// </summary>
	public bool IsEmpty => _entries.Count == 0;

	/// <summary>
	/// Gets the number of captured objects, including children.
	/// </summary>
	public int Count => _entries.Count;

	/// <summary>
	/// Gets the number of captured objects that have no captured
	/// parent.
	/// </summary>
	public int RootCount => _entries.Count(entry => entry.IsRoot);

	/// <summary>
	/// Captures a selection, replacing whatever the clipboard held.
	/// </summary>
	/// <param name="scene">The scene the objects belong to.</param>
	/// <param name="entityIds">The selected object ids.</param>
	/// <returns>The number of captured objects, including children.</returns>
	public int Capture(SceneDocument scene, IEnumerable<Guid> entityIds)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentNullException.ThrowIfNull(entityIds);

		_entries.Clear();

		foreach (SceneEntity root in SceneSelection.Roots(scene, entityIds))
		{
			CaptureSubtree(scene, root, parentSnapshotId: null, isRoot: true);
		}

		return _entries.Count;
	}

	/// <summary>
	/// Empties the clipboard.
	/// </summary>
	public void Clear()
	{
		_entries.Clear();
	}

	/// <summary>
	/// Creates detached objects for a paste, in parent-before-child
	/// order and with fresh identities.
	/// </summary>
	/// <remarks>
	/// Nothing is added to a scene: the caller inserts the returned
	/// objects, which keeps the paste undoable as one step and lets
	/// the objects go to whichever scene is open when it happens.
	/// </remarks>
	/// <returns>
	/// The objects to insert. The first object of each subtree has no
	/// parent; every other object points at an object in this list.
	/// </returns>
	public IReadOnlyList<SceneEntity> CreateEntities()
	{
		Dictionary<Guid, Guid> replacements = [];
		List<SceneEntity> created = [];

		foreach (ClipboardEntry entry in _entries)
		{
			Guid id = Guid.NewGuid();
			replacements.Add(entry.Snapshot.Id, id);

			SceneEntity entity = entry.Snapshot.Clone(id);

			entity.ParentId = entry.ParentSnapshotId is Guid parent
				? replacements[parent]
				: null;

			created.Add(entity);
		}

		return created;
	}

	private void CaptureSubtree(
		SceneDocument scene,
		SceneEntity entity,
		Guid? parentSnapshotId,
		bool isRoot)
	{
		SceneEntity snapshot = entity.Clone(Guid.NewGuid());
		snapshot.ParentId = parentSnapshotId;

		_entries.Add(new ClipboardEntry(
			snapshot,
			parentSnapshotId,
			isRoot));

		foreach (SceneEntity child in scene.GetChildren(entity.Id))
		{
			CaptureSubtree(scene, child, snapshot.Id, isRoot: false);
		}
	}

	private readonly record struct ClipboardEntry(
		SceneEntity Snapshot,
		Guid? ParentSnapshotId,
		bool IsRoot);
}
