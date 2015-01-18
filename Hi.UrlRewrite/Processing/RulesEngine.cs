﻿using Hi.UrlRewrite.Caching;
using Hi.UrlRewrite.Entities.Actions;
using Hi.UrlRewrite.Entities.Conditions;
using Hi.UrlRewrite.Entities.Match;
using Hi.UrlRewrite.Entities.Rules;
using Hi.UrlRewrite.Templates.Folders;
using Hi.UrlRewrite.Templates.Inbound;
using Hi.UrlRewrite.Templates.Outbound;
using Hi.UrlRewrite.Templates.Settings;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Publishing;
using Sitecore.SecurityModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Hi.UrlRewrite.Processing
{
    public class RulesEngine
    {

        private readonly Database db;

        public Database Database
        {
            get
            {
                return db;
            }
        }

        public RulesEngine(Database db)
        {
            this.db = db;
        }

        public List<InboundRule> GetInboundRules()
        {
            if (db == null)
            {
                return null;
            }

            var redirectFolderItems = GetRedirectFolderItems();

            if (redirectFolderItems == null)
            {
                return null;
            }

            var inboundRules = new List<InboundRule>();

            foreach (var redirectFolderItem in redirectFolderItems)
            {
                Log.Debug(this, db, "Loading Inbound Rules from RedirectFolder: {0}", redirectFolderItem.Name);

                var folderDescendants = redirectFolderItem.Axes.GetDescendants()
                    .Where(x => x.TemplateID == new ID(new Guid(SimpleRedirectItem.TemplateId)) ||
                                x.TemplateID == new ID(new Guid(InboundRuleItem.TemplateId)));

                foreach (var descendantItem in folderDescendants)
                {
                    if (descendantItem.TemplateID == new ID(new Guid(SimpleRedirectItem.TemplateId)))
                    {
                        var simpleRedirectItem = new SimpleRedirectItem(descendantItem);

                        Log.Debug(this, db, "Loading SimpleRedirect: {0}", simpleRedirectItem.Name);

                        var inboundRule = CreateInboundRuleFromSimpleRedirectItem(simpleRedirectItem, redirectFolderItem);

                        inboundRules.Add(inboundRule);
                    }
                    else if (descendantItem.TemplateID == new ID(new Guid(InboundRuleItem.TemplateId)))
                    {
                        var inboundRuleItem = new InboundRuleItem(descendantItem);

                        Log.Debug(this, db, "Loading InboundRule: {0}", inboundRuleItem.Name);

                        var inboundRule = CreateInboundRuleFromInboundRuleItem(inboundRuleItem, redirectFolderItem);

                        if (inboundRule != null && inboundRule.Enabled)
                        {
                            inboundRules.Add(inboundRule);
                        }
                    }
                }
            }

            return inboundRules;
        }

        public List<OutboundRule> GetOutboundRules()
        {
            if (db == null)
            {
                return null;
            }

            var redirectFolderItems = GetRedirectFolderItems();

            if (redirectFolderItems == null)
            {
                return null;
            }

            var outboundRules = new List<OutboundRule>();

            foreach (var redirectFolderItem in redirectFolderItems)
            {
                Log.Debug(this, db, "Loading Outbound Rules from RedirectFolder: {0}", redirectFolderItem.Name);

                var folderDescendants = redirectFolderItem.Axes.GetDescendants()
                    .Where(x => x.TemplateID == new ID(new Guid(OutboundRuleItem.TemplateId)));

                foreach (var descendantItem in folderDescendants)
                {
                    if (descendantItem.TemplateID == new ID(new Guid(OutboundRuleItem.TemplateId)))
                    {
                        var outboundRuleItem = new OutboundRuleItem(descendantItem);

                        Log.Debug(this, db, "Loading OutboundRule: {0}", outboundRuleItem.Name);

                        var outboundRule = CreateOutboundRuleFromOutboundRuleItem(outboundRuleItem, redirectFolderItem);

                        if (outboundRule != null && outboundRule.Enabled)
                        {
                            outboundRules.Add(outboundRule);
                        }
                    }
                }
            }

            return outboundRules;
        }

        private IEnumerable<Item> GetRedirectFolderItems()
        {
            var query = string.Format(Constants.RedirectFolderItemsQuery, Configuration.RewriteFolderSearchRoot,
                RedirectFolderItem.TemplateId);
            var redirectFolderItems = db.SelectItems(query);

            return redirectFolderItems;
        }

        internal InboundRule CreateInboundRuleFromSimpleRedirectItem(SimpleRedirectItem simpleRedirectItem, RedirectFolderItem redirectFolderItem)
        {
            var inboundRulePattern = string.Format("^{0}/?$", simpleRedirectItem.Path.Value);

            var siteNameRestriction = GetSiteNameRestriction(redirectFolderItem);

            var redirectTo = simpleRedirectItem.Target;
            string actionRewriteUrl;
            Guid? redirectItem;
            string redirectItemAnchor;

            GetRedirectUrlOrItemId(redirectTo, out actionRewriteUrl, out redirectItem, out redirectItemAnchor);

            Log.Debug(this, simpleRedirectItem.Database, "Creating Inbound Rule From Simple Redirect Item - {0} - id: {1} actionRewriteUrl: {2} redirectItem: {3}", simpleRedirectItem.Name, simpleRedirectItem.ID.Guid, actionRewriteUrl, redirectItem);

            var inboundRule = new InboundRule
            {
                Action = new Redirect
                {
                    AppendQueryString = true,
                    Name = "Redirect",
                    StatusCode = RedirectStatusCode.Permanent,
                    RewriteUrl = actionRewriteUrl,
                    RewriteItemId = redirectItem,
                    RewriteItemAnchor = redirectItemAnchor,
                    StopProcessingOfSubsequentRules = false,
                    HttpCacheability = HttpCacheability.NoCache
                },
                SiteNameRestriction = siteNameRestriction,
                Enabled = true,
                IgnoreCase = true,
                ItemId = simpleRedirectItem.ID.Guid,
                ConditionLogicalGrouping = LogicalGrouping.MatchAll,
                Name = simpleRedirectItem.Name,
                Pattern = inboundRulePattern,
                MatchType = MatchType.MatchesThePattern,
                Using = Using.RegularExpressions
            };

            return inboundRule;
        }

        internal InboundRule CreateInboundRuleFromInboundRuleItem(InboundRuleItem inboundRuleItem, RedirectFolderItem redirectFolderItem)
        {
            var siteNameRestriction = GetSiteNameRestriction(redirectFolderItem);
            var inboundRule = inboundRuleItem.ToInboundRule(siteNameRestriction);

            return inboundRule;
        }

        internal OutboundRule CreateOutboundRuleFromOutboundRuleItem(OutboundRuleItem outboundRuleItem,
            RedirectFolderItem redirectFolderItem)
        {
            var outboundRule = outboundRuleItem.ToOutboundRule();

            return outboundRule;
        }

        internal static string GetSiteNameRestriction(RedirectFolderItem redirectFolder)
        {
            var site = redirectFolder.SiteNameRestriction.Value;

            return site;
        }

        internal static void GetRedirectUrlOrItemId(LinkField redirectTo, out string actionRewriteUrl, out Guid? redirectItemId, out string redirectItemAnchor)
        {
            actionRewriteUrl = null;
            redirectItemId = null;
            redirectItemAnchor = null;

            if (redirectTo.TargetItem != null)
            {
                redirectItemId = redirectTo.TargetItem.ID.Guid;
                redirectItemAnchor = redirectTo.Anchor;
            }
            else
            {
                actionRewriteUrl = redirectTo.Url;
            }
        }

        #region Caching

        private RulesCache GetRulesCache()
        {
            return RulesCacheManager.GetCache(db);
        }

        internal List<InboundRule> GetCachedInboundRules()
        {
            var inboundRules = GetInboundRules();

            if (inboundRules != null)
            {
                Log.Info(this, db, "Adding {0} rules to Cache [{1}]", inboundRules.Count(), db.Name);

                var cache = GetRulesCache();
                cache.SetInboundRules(inboundRules);
            }
            else
            {
                Log.Info(this, db, "Found no rules");
            }

            return inboundRules;
        }

        internal List<OutboundRule> GetCachedOutboundRules()
        {
            var outboundRules = GetOutboundRules();

            if (outboundRules != null)
            {
                Log.Info(this, db, "Adding {0} rules to Cache [{1}]", outboundRules.Count(), db.Name);

                var cache = GetRulesCache();
                cache.SetOutboundRules(outboundRules);
            }
            else
            {
                Log.Info(this, db, "Found no rules");
            }

            return outboundRules;
        }

        internal void RefreshSimpleRedirect(Item item, Item redirectFolderItem)
        {
            UpdateRulesCache(item, redirectFolderItem, AddSimpleRedirect);
        }

        internal void RefreshInboundRule(Item item, Item redirectFolderItem)
        {
            UpdateRulesCache(item, redirectFolderItem, AddInboundRule);
        }

        internal void DeleteInboundRule(Item item, Item redirectFolderItem)
        {
            UpdateRulesCache(item, redirectFolderItem, RemoveInboundRule);
        }

        private void UpdateRulesCache(Item item, Item redirectFolderItem, Action<Item, Item, List<InboundRule>> action)
        {
            var cache = GetRulesCache();
            var inboundRules = cache.GetInboundRules();

            if (inboundRules == null)
            {
                inboundRules = GetInboundRules();
            }

            if (inboundRules != null)
            {
                action(item, redirectFolderItem, inboundRules);

                Log.Debug(this, item.Database, "Updating Rules Cache - count: {0}", inboundRules.Count);

                // update the cache
                cache.SetInboundRules(inboundRules);
            }
        }

        private void AddSimpleRedirect(Item item, Item redirectFolderItem, List<InboundRule> inboundRules)
        {

            Log.Debug(this, item.Database, "Adding Simple Redirect - item: [{0}]", item.Paths.FullPath);

            var simpleRedirectItem = new SimpleRedirectItem(item);
            var newInboundRule = CreateInboundRuleFromSimpleRedirectItem(simpleRedirectItem, redirectFolderItem);

            AddOrRemoveRule(item, redirectFolderItem, inboundRules, newInboundRule);
        }

        private void AddInboundRule(Item item, Item redirectFolderItem, List<InboundRule> inboundRules)
        {

            Log.Debug(this, item.Database, "Adding Inbound Rule - item: [{0}]", item.Paths.FullPath);

            var inboundRuleItem = new InboundRuleItem(item);

            var newInboundRule = CreateInboundRuleFromInboundRuleItem(inboundRuleItem, redirectFolderItem);

            if (newInboundRule != null)
            {
                AddOrRemoveRule(item, redirectFolderItem, inboundRules, newInboundRule);
            }
        }

        private void AddOrRemoveRule(Item item, Item redirectFolderItem, List<InboundRule> inboundRules, InboundRule newInboundRule)
        {
            if (newInboundRule.Enabled)
            {
                var existingInboundRule = inboundRules.FirstOrDefault(e => e.ItemId == item.ID.Guid);
                if (existingInboundRule != null)
                {

                    Log.Debug(this, item.Database, "Replacing Inbound Rule - item: [{0}]", item.Paths.FullPath);

                    var index = inboundRules.FindIndex(e => e.ItemId == existingInboundRule.ItemId);
                    inboundRules.RemoveAt(index);
                    inboundRules.Insert(index, newInboundRule);
                }
                else
                {

                    Log.Debug(this, item.Database, "Adding Inbound Rule - item: [{0}]", item.Paths.FullPath);

                    inboundRules.Add(newInboundRule);
                }
            }
            else
            {

                RemoveInboundRule(item, redirectFolderItem, inboundRules);
            }
        }

        private void RemoveInboundRule(Item item, Item redirectFolderItem, List<InboundRule> inboundRules)
        {
            Log.Debug(this, item.Database, "Removing Inbound Rule - item: [{0}]", item.Paths.FullPath);

            var existingInboundRule = inboundRules.FirstOrDefault(e => e.ItemId == item.ID.Guid);
            if (existingInboundRule != null)
            {
                inboundRules.Remove(existingInboundRule);
            }
        }

        #endregion

    }
}
