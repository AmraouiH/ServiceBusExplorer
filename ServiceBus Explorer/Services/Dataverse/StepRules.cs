using System;
using System.Collections.Generic;
using System.Linq;
using ServiceBusExplorer.Localization;
using ServiceBusExplorer.Models;

namespace ServiceBusExplorer.Services.Dataverse
{
    /// <summary>
    /// Registration rules of a step, checked before anything is sent to Dataverse
    /// so the user gets a clear message instead of a platform error.
    /// </summary>
    public static class StepRules
    {
        /// <summary>Input parameter holding the image, per message (same table as the Plugin Registration Tool).</summary>
        private static readonly Dictionary<string, string> ImageProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Assign"] = "Target",
            ["Create"] = "id",
            ["Delete"] = "Target",
            ["DeliverIncoming"] = "emailid",
            ["DeliverPromote"] = "emailid",
            ["Merge"] = "Target",
            ["Route"] = "Target",
            ["Send"] = "emailid",
            ["SetState"] = "entityMoniker",
            ["SetStateDynamicEntity"] = "entityMoniker",
            ["Update"] = "Target"
        };

        public static bool SupportsFilteringAttributes(string message) => string.Equals(message, "Update", StringComparison.OrdinalIgnoreCase);

        public static bool SupportsImages(string message) => message != null && ImageProperties.ContainsKey(message);

        public static string ImageProperty(string message) => message != null && ImageProperties.TryGetValue(message, out var value) ? value : null;

        public static bool SupportsPreImage(string message) => SupportsImages(message) && !string.Equals(message, "Create", StringComparison.OrdinalIgnoreCase);

        public static bool SupportsPostImage(string message, int stage) =>
            SupportsImages(message) && stage == StepStage.PostOperation && !string.Equals(message, "Delete", StringComparison.OrdinalIgnoreCase);

        /// <summary>Image types allowed for a message and a stage.</summary>
        public static List<int> AllowedImageTypes(string message, int stage)
        {
            var types = new List<int>();
            var pre = SupportsPreImage(message);
            var post = SupportsPostImage(message, stage);
            if (pre) types.Add(ImageKind.Pre);
            if (post) types.Add(ImageKind.Post);
            if (pre && post) types.Add(ImageKind.Both);
            return types;
        }

        /// <summary>Blocking problems of a step; empty when it can be saved.</summary>
        public static List<string> Validate(StepInfo step)
        {
            var issues = new List<string>();

            if (string.IsNullOrWhiteSpace(step.Message)) issues.Add(Strings.RuleMessageRequired);
            if (step.Mode == StepMode.Asynchronous && step.Stage != StepStage.PostOperation) issues.Add(Strings.RuleAsyncPostOnly);
            if (step.Rank < 1) issues.Add(Strings.RuleRank);
            if (!string.IsNullOrEmpty(step.FilteringAttributes) && !SupportsFilteringAttributes(step.Message)) issues.Add(Strings.RuleFilteringUpdateOnly);

            if (step.Images.Count > 0 && !SupportsImages(step.Message))
            {
                issues.Add(Strings.RuleNoImages(step.Message));
                return issues;
            }

            var allowed = AllowedImageTypes(step.Message, step.Stage);
            foreach (var image in step.Images)
            {
                if (string.IsNullOrWhiteSpace(image.Alias)) issues.Add(Strings.RuleImageAlias(image.Name));
                if (!allowed.Contains(image.ImageType)) issues.Add(Strings.RuleImageType(image.Alias ?? image.Name, Strings.ImageTypeLabel(image.ImageType), step.Message, Strings.StageLabel(step.Stage)));
            }

            foreach (var duplicate in step.Images.GroupBy(i => i.Alias ?? string.Empty, StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                issues.Add(Strings.RuleImageDuplicate(duplicate.Key));
            }
            return issues;
        }

        /// <summary>Lines describing what a save changes, for the confirmation popup.</summary>
        public static List<string> Describe(StepChange change)
        {
            var o = change.Original;
            var d = change.Draft;
            var lines = new List<string>();

            void Compare(string label, string before, string after)
            {
                if (!string.Equals(before ?? string.Empty, after ?? string.Empty, StringComparison.Ordinal))
                {
                    lines.Add($"{label} : {Display(before)}  →  {Display(after)}");
                }
            }

            Compare(Strings.FieldMessage, o.Message, d.Message);
            Compare(Strings.FieldTable, o.Table, d.Table);
            Compare(Strings.FieldStage, Strings.StageLabel(o.Stage), Strings.StageLabel(d.Stage));
            Compare(Strings.FieldMode, Strings.ModeLabel(o.Mode), Strings.ModeLabel(d.Mode));
            Compare(Strings.FieldRank, o.Rank.ToString(), d.Rank.ToString());
            Compare(Strings.FieldFiltering, o.FilteringAttributes, d.FilteringAttributes);
            Compare(Strings.FieldDeleteJob, o.DeleteJobOnSuccess ? Strings.Yes : Strings.No, d.DeleteJobOnSuccess ? Strings.Yes : Strings.No);
            Compare(Strings.FieldName, o.Name, d.Name);
            Compare(Strings.FieldDescription, o.Description, d.Description);

            foreach (var image in d.Images.Where(i => i.Id == Guid.Empty))
            {
                lines.Add(Strings.ChangeImageAdded(ImageSummary(image)));
            }

            foreach (var image in d.Images.Where(i => i.Id != Guid.Empty))
            {
                var before = o.Images.FirstOrDefault(i => i.Id == image.Id);
                if (before != null && !SameImage(before, image)) lines.Add(Strings.ChangeImageUpdated(ImageSummary(before), ImageSummary(image)));
            }

            foreach (var image in o.Images.Where(i => d.Images.All(x => x.Id != i.Id)))
            {
                lines.Add(Strings.ChangeImageRemoved(ImageSummary(image)));
            }
            return lines;
        }

        public static bool SameImage(ImageInfo a, ImageInfo b) =>
            a.ImageType == b.ImageType && a.Name == b.Name && a.Alias == b.Alias && NormalizeColumns(a.Attributes) == NormalizeColumns(b.Attributes);

        /// <summary>Sorted, trimmed, comma-separated list, with no duplicates.</summary>
        public static string NormalizeColumns(string columns)
        {
            var items = (columns ?? string.Empty).Split(',').Select(c => c.Trim().ToLowerInvariant()).Where(c => c.Length > 0).Distinct().OrderBy(c => c, StringComparer.Ordinal);
            return string.Join(",", items);
        }

        private static string ImageSummary(ImageInfo image) =>
            $"{image.Alias} ({Strings.ImageTypeLabel(image.ImageType)}, {(image.AllColumns ? Strings.AllColumns : image.Attributes)})";

        private static string Display(string value) => string.IsNullOrEmpty(value) ? Strings.EmptyValue : value;
    }
}
