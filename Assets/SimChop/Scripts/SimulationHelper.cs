using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimulationHelper
{
    static readonly int orderPosId = Shader.PropertyToID("_Positions"),
    orderResId = Shader.PropertyToID("_Resolution"),
    orderMatrixId = Shader.PropertyToID("_Transform");
    public static uint fToI(float num, int precision){
      return (uint)(num*Mathf.Pow(10,precision));
    }

    public static string iToBin(int num){
      return System.Convert.ToString(num, 2);
    }

    public static int getLargest(int i, int j, int k){
      int largest = 0;
      if(i > j){
        largest = i;
      }
      else{
        largest = j;
      }
      if(largest < k){
        largest = k;
      }
      return largest;
    }

    public static string addChar(string s1, int index){
      if(index < s1.Length){
        return "" + s1[index];
      }
      return "";
    }
    public static string interleaveString(string s1, string s2, string s3){
      string result = "";
      int largest = getLargest(s1.Length, s2.Length, s3.Length);
      for(int i =0; i < largest; i++){
          result += addChar(s1, i);
          result += addChar(s2, i);
          result += addChar(s3, i);
      }
      return result;
    }
    public static ulong interleave(int s1, int s2, int s3){
      ulong result = 0x00000000;
      Debug.Log("decimal: (" + s1 + ", " + s2 + ", " + s3 + ")");
      for(int i = 0; i < 32; i++){
        int offset = (0x00000001<<(i));
        Debug.Log(i+" offset = " + offset);
        result |= (ulong)(offset & s3) << (i*2);
        Debug.Log(i + " s3: " + result);
        result |= (ulong)(offset & s2) << (i*2+1);
        Debug.Log(i + " s2: " + result);
        result |= (ulong)(offset & s1) << (i*2+2);
        Debug.Log(i+ " s1: " + result);
        Debug.Log(i + " ulong loop: " + result);
      }
      return result;
    }

    public static Matrix4x4 createMatrixScale(float width, float height, float depth) {
      return Matrix4x4.Scale(
  			new Vector3(
  				1.0f/width,
  				1.0f/height,
  				1.0f/depth
  			)
  		);
    }
    
    public static Matrix4x4 createMatrixMapToTwoCube(int precision) {
      return
        Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0));
    }
    
    public static Matrix4x4 createMatrixMapToUnitCube(Matrix4x4 unitScale, float zTranslation) {
      // we only want the points in front of the camera anyway, so no need to shift z at all to the unit cube
      return 
        unitScale * // scale region down to unit cube, but this will be centred at the origin
        Matrix4x4.Rotate(Camera.main.transform.rotation).inverse * // undo the camera rotation
        Matrix4x4.Translate(-1*(Camera.main.transform.position) - Camera.main.transform.forward*zTranslation); // camera location to local coordinates
    }

    public static void clear(ref byte[] colors, ref int[] clear_data, ref byte[] clear_colors, ref int[] data, int N)
  	{
  		System.Array.Copy(clear_data, data, N);
  		System.Array.Copy(clear_colors, colors, 8 * N);
  	}
}
