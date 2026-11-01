using System.Globalization;
using System.Numerics;

namespace Volt.Editor.Core.Assets;

/// <summary>
/// A triangle mesh loaded from a project file.
/// </summary>
/// <param name="Id">The project-relative asset identifier.</param>
/// <param name="Positions">The vertex positions, in local space.</param>
/// <param name="Indices">
/// Three position indices per triangle. The loader converts polygons to
/// triangles, so a renderer only ever expands triangles.
/// </param>
/// <param name="Center">The centre of the local-space bounds.</param>
/// <param name="HalfExtent">
/// The half extent of the local-space bounds along each axis. It is the
/// picking box for an imported mesh, exactly as a built-in primitive's
/// half extent is for that primitive.
/// </param>
public sealed record ImportedMesh(
	string Id,
	IReadOnlyList<Vector3> Positions,
	IReadOnlyList<int> Indices,
	Vector3 Center,
	Vector3 HalfExtent)
{
	/// <summary>Gets how many triangles the mesh contains.</summary>
	public int TriangleCount => Indices.Count / 3;
}

/// <summary>
/// Reads Wavefront OBJ geometry into vertex positions and triangles.
/// </summary>
/// <remarks>
/// <para>
/// OBJ is the format a developer is most likely to already have, and it
/// is plain text, so importing it needs no new dependency. Only what the
/// renderer can draw is read: positions and faces. Normals, texture
/// coordinates, materials, and smoothing groups are ignored rather than
/// silently misread.
/// </para>
/// <para>
/// Faces may have any number of corners and are triangulated as a fan.
/// Indices may be positive (1-based) or negative (relative to the
/// vertices seen so far), and the <c>v/vt/vn</c> form is accepted with
/// the extra components discarded.
/// </para>
/// </remarks>
public static class ObjMeshLoader
{
	/// <summary>
	/// Loads one OBJ file.
	/// </summary>
	/// <param name="id">The project-relative identifier to record.</param>
	/// <param name="fullPath">The file to read.</param>
	/// <returns>The loaded mesh.</returns>
	/// <exception cref="InvalidDataException">
	/// The file is empty, malformed, or contains no triangles.
	/// </exception>
	public static ImportedMesh Load(string id, string fullPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);

		using StreamReader reader = new(fullPath);

		return Parse(id, reader);
	}

	/// <summary>
	/// Parses OBJ text.
	/// </summary>
	/// <param name="id">The project-relative identifier to record.</param>
	/// <param name="reader">The text to read.</param>
	/// <returns>The parsed mesh.</returns>
	/// <exception cref="InvalidDataException">
	/// The text is empty, malformed, or contains no triangles.
	/// </exception>
	public static ImportedMesh Parse(string id, TextReader reader)
	{
		ArgumentNullException.ThrowIfNull(reader);

		List<Vector3> positions = [];
		List<int> indices = [];
		HashSet<int> used = [];
		string? line;
		int lineNumber = 0;

		while ((line = reader.ReadLine()) is not null)
		{
			lineNumber++;

			ReadOnlySpan<char> span = line.AsSpan().Trim();

			if (span.IsEmpty || span[0] == '#')
			{
				continue;
			}

			if (span.StartsWith("v ", StringComparison.Ordinal))
			{
				positions.Add(ParsePosition(span[2..], lineNumber));
				continue;
			}

			if (span.StartsWith("f ", StringComparison.Ordinal))
			{
				AddFace(span[2..], positions.Count, indices, used, lineNumber);
			}
		}

		if (indices.Count == 0)
		{
			throw new InvalidDataException(
				$"'{id}' contains no triangles.");
		}

		foreach (int index in indices)
		{
			used.Add(index);
		}

		Vector3 minimum = new(float.PositiveInfinity);
		Vector3 maximum = new(float.NegativeInfinity);

		foreach (int index in used)
		{
			minimum = Vector3.Min(minimum, positions[index]);
			maximum = Vector3.Max(maximum, positions[index]);
		}

		Vector3 center = (minimum + maximum) * 0.5f;
		Vector3 halfExtent = Vector3.Max(
			(maximum - minimum) * 0.5f,
			new Vector3(0.001f));

		return new ImportedMesh(
			id,
			positions,
			indices,
			center,
			halfExtent);
	}

	private static Vector3 ParsePosition(
		ReadOnlySpan<char> values,
		int lineNumber)
	{
		Span<float> components = stackalloc float[3];

		for (int index = 0; index < 3; index++)
		{
			ReadOnlySpan<char> rest = values.TrimStart();
			int end = rest.IndexOf(' ');

			ReadOnlySpan<char> token = end < 0 ? rest : rest[..end];

			if (!float.TryParse(
				token,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out components[index]) ||
				!float.IsFinite(components[index]))
			{
				throw new InvalidDataException(
					$"Line {lineNumber} has an invalid vertex position.");
			}

			values = end < 0 ? ReadOnlySpan<char>.Empty : rest[(end + 1)..];
		}

		return new Vector3(components[0], components[1], components[2]);
	}

	private static void AddFace(
		ReadOnlySpan<char> values,
		int vertexCount,
		List<int> indices,
		HashSet<int> used,
		int lineNumber)
	{
		Span<int> corners = stackalloc int[16];
		int count = 0;

		while (true)
		{
			ReadOnlySpan<char> rest = values.TrimStart();

			if (rest.IsEmpty)
			{
				break;
			}

			int end = rest.IndexOf(' ');
			ReadOnlySpan<char> token = end < 0 ? rest : rest[..end];

			if (count == corners.Length)
			{
				throw new InvalidDataException(
					$"Line {lineNumber} has a face with too many corners.");
			}

			corners[count++] = ResolveIndex(
				token,
				vertexCount,
				lineNumber);

			values = end < 0 ? ReadOnlySpan<char>.Empty : rest[(end + 1)..];
		}

		if (count < 3)
		{
			throw new InvalidDataException(
				$"Line {lineNumber} has a face with fewer than three corners.");
		}

		for (int corner = 1; corner < count - 1; corner++)
		{
			indices.Add(corners[0]);
			indices.Add(corners[corner]);
			indices.Add(corners[corner + 1]);

			used.Add(corners[0]);
			used.Add(corners[corner]);
			used.Add(corners[corner + 1]);
		}
	}

	private static int ResolveIndex(
		ReadOnlySpan<char> token,
		int vertexCount,
		int lineNumber)
	{
		int slash = token.IndexOf('/');

		if (slash >= 0)
		{
			token = token[..slash];
		}

		if (!int.TryParse(
			token,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out int value) ||
			value == 0)
		{
			throw new InvalidDataException(
				$"Line {lineNumber} names an invalid vertex index.");
		}

		int index = value > 0 ? value - 1 : vertexCount + value;

		if (index < 0 || index >= vertexCount)
		{
			throw new InvalidDataException(
				$"Line {lineNumber} names vertex {value}, which does not exist.");
		}

		return index;
	}
}
