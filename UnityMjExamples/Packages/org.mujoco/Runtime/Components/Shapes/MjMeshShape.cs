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
using System.Linq;
using System.Xml;
using UnityEngine;

namespace Mujoco {

[Serializable]
public class MjMeshShape : IMjShape {

  public Mesh Mesh;

  public void ToMjcf(XmlElement mjcf, Transform transform) {
    var scene = MjScene.Instance;
    if (Mesh == null && transform != null) {
      TryAssignMesh(transform.GetComponent<MeshFilter>()?.sharedMesh);
      TryAssignMesh(transform.GetComponent<MeshCollider>()?.sharedMesh);
      TryAssignMesh(transform.GetComponent<SkinnedMeshRenderer>()?.sharedMesh);
    }
    if (Mesh == null) {
      var objectName = transform != null ? transform.name : "<unknown>";
      throw new Exception($"Mesh geom '{objectName}' has no Mesh assigned.");
    }
    if (Mesh.vertexCount == 0) {
      var objectName = transform != null ? transform.name : "<unknown>";
      throw new Exception(
        $"Mesh geom '{objectName}' has zero vertices. Ensure the mesh is valid and readable.");
    }
    var assetName = scene.GenerationContext.AddMeshAsset(Mesh);
    mjcf.SetAttribute("mesh", assetName);
  }

  private void TryAssignMesh(Mesh candidate) {
    if (Mesh != null || candidate == null) {
      return;
    }
    if (candidate.vertexCount == 0) {
      return;
    }
    Mesh = candidate;
  }

  public void FromMjcf(XmlElement mjcf) {
    // When the asset was parsed and stored its name was sanitized, so we should load it using a
    // sanitized name:
    var assetName = MjEngineTool.Sanitize(
        mjcf.GetStringAttribute("mesh", defaultValue: string.Empty));
    if (!string.IsNullOrEmpty(assetName)) {
      Mesh = Resources.Load<Mesh>(assetName);
      if (Mesh == null) {
        var prefab = Resources.Load<GameObject>(assetName);
        if (prefab != null) {
          var filter = prefab.GetComponent<MeshFilter>();
          if (filter != null) {
            Mesh = filter.sharedMesh;
          } else {
            var skinned = prefab.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinned != null) {
              Mesh = skinned.sharedMesh;
            }
          }
        }
      }
      if (Mesh == null) {
        Debug.LogWarning($"Failed to load mesh resource '{assetName}'.");
      }
    }
  }

  public Tuple<Vector3[], int[]> BuildMesh() {
    return Tuple.Create(Mesh.vertices, Mesh.triangles);
  }

  public Vector4 GetChangeStamp() {
    return Vector4.one;
  }

  public void DebugDraw(Transform transform) {
    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
    Gizmos.DrawWireMesh(Mesh);
  }
}
}
