
namespace QuantiX.Engine.Mathematics.Vectors
{
    [System.AttributeUsage(System.AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    internal sealed class GenerateVectorAttribute : System.Attribute
    {
        public int Dimensions { get; }
        public int SimdSlots { get; set; }
        public bool HasCross { get; set; }
        public string CrossResult { get; set; }

        public GenerateVectorAttribute(int dimensions)
        {
            Dimensions = dimensions;
            SimdSlots = dimensions;
            CrossResult = "";
        }
    }
}
