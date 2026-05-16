using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AnakinAnalytics;

namespace AnakinAnalytics.Test
{
    [TestClass]
    public class ExtensionsTest
    {
        public static IEnumerable<object[]> IsUpperData => new List<object[]>
        {
            new object[] { ":D", true },
            new object[] { ":d", false },
            new object[] { ":)", false },
            new object[] { "Hello", false },
        };

        [DataTestMethod]
        [DynamicData(nameof(IsUpperData), DynamicDataSourceType.Property)]
        public void IsUpperTest(string text, bool expected)
        {
            Assert.AreEqual(expected, text.IsUpper());
        }
    }
}
