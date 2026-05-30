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
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Mujoco {

// Minimal OBJ parser that supports vertex positions and face indices.
public static class ObjMeshParser {

  private const int _unityLimitNumVerticesPerMesh = 65535;

  private static Vector3 ToXZY(Vector3 v) => new Vector3(v.x, v.z, v.y);

  public static Mesh Parse(byte[] objFileContents, Vector3 scale) {
    if (objFileContents == null || objFileContents.Length == 0) {
      throw new IOException("OBJ file is empty.");
    }

    var vertices = new List<Vector3>();
    var triangles = new List<int>();

    var separators = new[] {' ', '\t'};
    using (var stream = new MemoryStream(objFileContents)) {
      using (var reader = new StreamReader(stream)) {
        string line;
        while ((line = reader.ReadLine()) != null) {
          var trimmed = line.Trim();
          if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) {
            continue;
          }
          var parts = trimmed.Split(separators, StringSplitOptions.RemoveEmptyEntries);
          if (parts.Length == 0) {
            continue;
          }
          if (string.Equals(parts[0], "v", StringComparison.OrdinalIgnoreCase)) {
            if (parts.Length >= 4) {
              var vertex = new Vector3(
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture),
                float.Parse(parts[3], CultureInfo.InvariantCulture));
              vertices.Add(ToXZY(Vector3.Scale(vertex, scale)));
            }
          } else if (string.Equals(parts[0], "f", StringComparison.OrdinalIgnoreCase)) {
            if (parts.Length < 4) {
              continue;
            }
            var faceIndices = new int[parts.Length - 1];
            var validCount = 0;
            for (var i = 1; i < parts.Length; i++) {
              var indexToken = parts[i];
              if (string.IsNullOrEmpty(indexToken)) {
                continue;
              }
              var idxStr = indexToken.Split('/')[0];
              if (string.IsNullOrEmpty(idxStr)) {
                continue;
              }
              var idx = int.Parse(idxStr, CultureInfo.InvariantCulture);
              if (idx < 0) {
                idx = vertices.Count + idx;
              } else {
                idx = idx - 1;
              }
              if (idx >= 0 && idx < vertices.Count) {
                faceIndices[validCount++] = idx;
              }
            }
            if (validCount < 3) {
              continue;
            }
            for (var i = 1; i < validCount - 1; i++) {
              var a = faceIndices[0];
              var b = faceIndices[i];
              var c = faceIndices[i + 1];
              triangles.Add(a);
              triangles.Add(c);
              triangles.Add(b);
            }
          }
        }
      }
    }

    if (vertices.Count == 0) {
      throw new IOException("OBJ contains no vertices or uses an unsupported format.");
    }
    if (triangles.Count == 0) {
      throw new IOException("OBJ contains no faces or uses an unsupported format.");
    }
    if (vertices.Count > _unityLimitNumVerticesPerMesh) {
      throw new IndexOutOfRangeException(
          "The mesh exceeds the number of vertices per mesh allowed by Unity. " +
          $"({vertices.Count} > {_unityLimitNumVerticesPerMesh})");
    }

    var mesh = new Mesh();
    mesh.vertices = vertices.ToArray();
    mesh.triangles = triangles.ToArray();
    mesh.RecalculateNormals();
    mesh.RecalculateTangents();
    mesh.RecalculateBounds();
    return mesh;
  }
}
}
