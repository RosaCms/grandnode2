﻿using Grand.Infrastructure.ModelBinding;
using Grand.Infrastructure.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Grand.Web.Admin.Models.Tasks;

public class ScheduleTaskModel : BaseEntityModel
{
    [GrandResourceDisplayName("Admin.System.ScheduleTasks.ScheduleTaskName")]
    public string ScheduleTaskName { get; set; }

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.LeasedByMachineName")]
    public string LeasedByMachineName { get; set; }    

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.Enabled")]
    public bool Enabled { get; set; }

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.StopOnError")]
    public bool StopOnError { get; set; }

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.LastStartUtc")]
    public DateTime? LastStartUtc { get; set; }

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.LastEndUtc")]
    public DateTime? LastEndUtc { get; set; }

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.LastSuccessUtc")]
    public DateTime? LastSuccessUtc { get; set; }

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.TimeInterval")]
    public int TimeInterval { get; set; }

    [GrandResourceDisplayName("Admin.System.ScheduleTasks.StoreId")]
    public string StoreId { get; set; }

    public IList<SelectListItem> AvailableStores { get; set; } = new List<SelectListItem>();
}