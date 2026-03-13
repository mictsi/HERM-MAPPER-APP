using System.ComponentModel.DataAnnotations;

namespace HERMMapperApp.Models;

public enum MappingStatus
{
    [Display(Name = "Draft")]
    Draft = 0,

    [Display(Name = "In Review")]
    InReview = 1,

    [Display(Name = "Complete")]
    Complete = 2,

    [Display(Name = "Out of Scope")]
    OutOfScope = 3
}
