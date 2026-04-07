using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace SampleManagement;

//TODO Eventually there will be more here, but the match is too strict at the moment, so it's easier to keep the actual validation attributes out

/// <summary>
/// Marks a property that should not be displayed in UniversalTable
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class NotDisplayedAttribute : Attribute
{
}
