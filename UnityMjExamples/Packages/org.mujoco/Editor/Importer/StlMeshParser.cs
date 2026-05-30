// Copyright 2019 DeepMind Technologies Limited
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Mujoco {

public static class BinaryReaderExtensions {
  public static Vector3 ReadVector3(this BinaryReader reader) {
    var x = reader.ReadSingle();
    var y = reader.ReadSingle();
    var z = reader.ReadSingle();
    return new Vector3(x, y, z);
  }
}

public static class BinaryWriterExtensions {
  public static void Write(this BinaryWriter writer, Vector3 val) {
    writer.Write(val.x);
    writer.Write(val.y);
    writer.Write(val.z);
  }
}

public class StlMeshParser {

  private const int _headerLength = 80;
  private const int _attributesSizeLength = 2;
  private const int _verticesPerTriangle = 3;
  private const int _unityLimitNumVerticesPerMesh = 65535;
  private const string _asciiFileTypeId = "solid";
  private const int _binaryStlHeaderLength = 80;
  private const int _binaryStlTriangleCountLength = 4;
  private const int _binaryStlTriangleStride = 50;

  private static Vector3 ToXZY(Vector3 v) => new Vector3(v.x, v.z, v.y);

  // The binary STL format is described here: https://en.wikipedia.org/wiki/STL_(file_format)
  public static Mesh ParseBinary(byte[] stlFileContents, Vector3 scale) {
    if (stlFileContents == null ||
        stlFileContents.Length < _binaryStlHeaderLength + _binaryStlTriangleCountLength) {
      throw new IOException("STL file is too small to be valid.");
    }

    var fileTypeId = System.Text.Encoding.UTF8.GetString(
      stlFileContents.Take(_asciiFileTypeId.Length).ToArray());

    // Some binary STL files start with "solid" in the header. Validate by length before
    // deciding the format.
    var numTriangles = BitConverter.ToUInt32(stlFileContents, _binaryStlHeaderLength);
    var expectedBinaryLength =
      (long)_binaryStlHeaderLength + _binaryStlTriangleCountLength +
      (long)numTriangles * _binaryStlTriangleStride;
    var isProbablyBinary = expectedBinaryLength == stlFileContents.Length;

    if (fileTypeId == _asciiFileTypeId && !isProbablyBinary) {
      return ParseAscii(stlFileContents, scale);
    }
    if (!isProbablyBinary && fileTypeId != _asciiFileTypeId) {
      // The header doesn't indicate ASCII, but the size doesn't match binary STL either.
      // Try parsing as ASCII before failing.
      return ParseAscii(stlFileContents, scale);
    }

    using (var stream = new MemoryStream(stlFileContents)) {
      using (var reader = new BinaryReader(stream)) {
        reader.ReadBytes(_headerLength);
        numTriangles = reader.ReadUInt32();
        var numVertices = numTriangles * _verticesPerTriangle;
        if (numVertices > _unityLimitNumVerticesPerMesh) {
          throw new IndexOutOfRangeException(
              "The mesh exceeds the number of vertices per mesh allowed by Unity. " +
              $"({numVertices} > {_unityLimitNumVerticesPerMesh})");
        }
        if (expectedBinaryLength > stlFileContents.Length) {
          throw new IOException(
            "STL file length does not match the binary STL specification.");
        }
        var triangleIndices = new List<int>(capacity: (int)numVertices);
        var vertices = new List<Vector3>(capacity: (int)numVertices);
        var normals = new List<Vector3>(capacity: (int)numVertices);
        for (var i = 0; i < numVertices; i += _verticesPerTriangle) {
          var triangleNormal = ToXZY(reader.ReadVector3());
          normals.AddRange(new[] { triangleNormal, triangleNormal, triangleNormal });
          vertices.AddRange(new[] {
            ToXZY(reader.ReadVector3()),
            ToXZY(reader.ReadVector3()),
            ToXZY(reader.ReadVector3()) });
          triangleIndices.AddRange(new[] {i, i + 2, i + 1});
          reader.ReadInt16();  // Read the unused attribute indices field.
        }

        var mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.triangles = triangleIndices.ToArray();
        mesh.vertices = mesh.vertices.Select(
            vertexPosition => Vector3.Scale(vertexPosition, scale)).ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
      }
    }
  }

  private static Mesh ParseAscii(byte[] stlFileContents, Vector3 scale) {
    var vertices = new List<Vector3>();
    var normals = new List<Vector3>();
    var triangleIndices = new List<int>();

    using (var stream = new MemoryStream(stlFileContents)) {
      using (var reader = new StreamReader(stream)) {
        var currentNormal = Vector3.zero;
        var vertexCountInFacet = 0;
        string line;
        while ((line = reader.ReadLine()) != null) {
          var trimmed = line.Trim();
          if (trimmed.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase)) {
            var parts = trimmed.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5) {
              currentNormal = ToXZY(new Vector3(
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture),
                float.Parse(parts[4], CultureInfo.InvariantCulture)));
            }
            vertexCountInFacet = 0;
          } else if (trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) {
            var parts = trimmed.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 4) {
              vertices.Add(ToXZY(new Vector3(
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture))));
              normals.Add(currentNormal);
              vertexCountInFacet++;
              if (vertexCountInFacet == _verticesPerTriangle) {
                var i = vertices.Count - _verticesPerTriangle;
                triangleIndices.AddRange(new[] {i, i + 2, i + 1});
                vertexCountInFacet = 0;
              }
            }
          }
        }
      }
    }

    if (vertices.Count > _unityLimitNumVerticesPerMesh) {
      throw new IndexOutOfRangeException(
          "The mesh exceeds the number of vertices per mesh allowed by Unity. " +
          $"({vertices.Count} > {_unityLimitNumVerticesPerMesh})");
    }
    if (vertices.Count == 0) {
      throw new IOException("ASCII STL contains no vertices. The file may not be STL.");
    }

    var mesh = new Mesh();
    mesh.vertices = vertices.ToArray();
    mesh.normals = normals.ToArray();
    mesh.triangles = triangleIndices.ToArray();
    mesh.vertices = mesh.vertices.Select(
        vertexPosition => Vector3.Scale(vertexPosition, scale)).ToArray();
    mesh.RecalculateNormals();
    mesh.RecalculateTangents();
    mesh.RecalculateBounds();
    return mesh;
  }

  public static byte[] SerializeBinary(Mesh mesh) {
    using (var stream = new MemoryStream()) {
      using (var writer = new BinaryWriter(stream)) {
        // Write the header. We only want to write the id and then pad the rest with zeros, up to 80
        // bytes.
        writer.Write(_asciiFileTypeId);
        writer.Write(new byte[_headerLength - _asciiFileTypeId.Length - 1]);

        var numTriangles = mesh.triangles.Length / 3;
        writer.Write((int)numTriangles);

        for (var i = 0; i < mesh.triangles.Length; i += _verticesPerTriangle) {
          // STL format uses face normals, while Unity Meshes use vertex normals. We need to convert
          // one into another by calculating a mean of vertex normals.
          var i1 = mesh.triangles[i];
          var i2 = mesh.triangles[i + 1];
          var i3 = mesh.triangles[i + 2];
          var faceNormal = (mesh.normals[i1] + mesh.normals[i2] + mesh.normals[i3]).normalized;
          writer.Write(ToXZY(faceNormal));

          writer.Write(ToXZY(mesh.vertices[i1]));
          writer.Write(ToXZY(mesh.vertices[i3]));
          writer.Write(ToXZY(mesh.vertices[i2]));

          writer.Write((short)0);
        }
        return stream.ToArray();
      }
    }
  }
}
}
