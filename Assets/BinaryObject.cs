using UnityEngine;

namespace DefaultNamespace
{
	[CreateAssetMenu(fileName = "BinaryObject", menuName = "Binary Object", order = 0)]
	[PreferBinarySerialization]
	public class BinaryObject : ScriptableObject
	{
		public string data;
	}
}