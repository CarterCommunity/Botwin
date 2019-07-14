namespace Carter.Tests.Modelbinding
{
    using System;
    using System.Collections.Generic;

    public class TestModel
    {
        public int MyIntProperty { get; set; }

        public string MyStringProperty { get; set; }

        public double MyDoubleProperty { get; set; }

        public string[] MyArrayProperty { get; set; }

        public IEnumerable<int> MyIntArrayProperty { get; set; }

        public IEnumerable<int> MyIntListProperty { get; set; }

        public Guid MyGuidProperty { get; set; }

        public bool MyBoolProperty { get; set; }

        public DateTime MyDateTimeProperty { get; set; }

        public bool? MyNullableBoolProperty { get; set; }

        public int? MyNullableIntProperty { get; set; }

        public DateTime? MyNullableDataTimeProperty { get; set; }
    }
}