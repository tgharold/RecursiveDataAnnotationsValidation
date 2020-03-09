namespace RecursiveDataAnnotationsValidation.Extensions
{
    internal static class ObjectExtensions
    {
        public static object GetPropertyValue(this object obj, string propertyName)
        {
            object objValue = null;

            var propertyInfo = obj?.GetType().GetProperty(propertyName);
            if (propertyInfo != null)
                objValue = propertyInfo.GetValue(obj, null);

            return objValue;
        }
    }
}