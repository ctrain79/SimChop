using UnityEngine;

public class SimulationHelper
{		
	public static Matrix4x4 createMatrixScale(float width, float height, float depth) {
		return Matrix4x4.Scale(
			new Vector3(
				1.0f/width,
				1.0f/height,
				1.0f/depth
			)
		);
	}
	
	public static Matrix4x4 createMatrixMapToPosOctant() {
		// shift unit cube so that it is in first octant (all points mapped inside are positive coordinate values)
		return
			Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0));
	}
	
	public static Matrix4x4 createMatrixMapToUnitCube(Matrix4x4 unitScale, float zTranslation) {
		return 
			unitScale * // scale region down to unit cube, but this will be centred at the origin
			Matrix4x4.Rotate(Camera.main.transform.rotation).inverse * // undo the camera rotation
			Matrix4x4.Translate(-1*(Camera.main.transform.position) - Camera.main.transform.forward*zTranslation); // camera location to local coordinates
	}
}
