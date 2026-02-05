using System;
using System.Collections.Generic;
using System.Text;

namespace EShop.BuildingBlocks.Domain
{
    [AttributeUsage(AttributeTargets.Property)]
    public class SensitiveDataAttribute : Attribute
    {
    }
}
