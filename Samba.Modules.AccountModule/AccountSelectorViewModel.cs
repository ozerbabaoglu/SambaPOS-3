﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Linq.Expressions;
using System.Windows;
using Microsoft.Practices.Prism.Events;
using Samba.Domain.Models;
using Samba.Domain.Models.Accounts;
using Samba.Infrastructure.Settings;
using Samba.Localization;
using Samba.Localization.Properties;
using Samba.Presentation.Common;
using Samba.Presentation.Common.Commands;
using Samba.Presentation.Services;
using Samba.Presentation.Services.Common;
using Samba.Services;

namespace Samba.Modules.AccountModule
{
    public class AccountRowData
    {
        public AccountRowData(string name, decimal balance, decimal exchange, int accountId, string currencyFormat, int accountTypeId, string groupKey)
        {
            Name = name;
            Balance = balance;
            if (!string.IsNullOrEmpty(currencyFormat)) Exchange = exchange;
            CurrencyFormat = currencyFormat;
            AccountId = accountId;
            AccountTypeId = accountTypeId;
            GroupKey = groupKey;
        }

        protected string CurrencyFormat { get; set; }
        public int AccountId { get; set; }
        public string BalanceStr
        {
            get
            {
                return !string.IsNullOrEmpty(ExchangeStr)
                    ? ExchangeStr
                    : Balance.ToString(LocalSettings.ReportCurrencyFormat);
            }
        }
        public string ExchangeStr { get { return Exchange != Balance ? string.Format(CurrencyFormat, Exchange) : ""; } }
        public string Name { get; set; }
        public decimal Balance { get; set; }
        public decimal Exchange { get; set; }
        public string Fill { get; set; }
        public int AccountTypeId { get; set; }
        public string GroupKey { get; set; }
    }

    [Export]
    public class AccountSelectorViewModel : ObservableObject
    {
        private readonly IAccountService _accountService;
        private readonly ICacheService _cacheService;
        private readonly IApplicationState _applicationState;
        private readonly IPrinterService _printerService;
        private AccountScreen _selectedAccountScreen;

        public event EventHandler Refreshed;

        protected virtual void OnRefreshed()
        {
            EventHandler handler = Refreshed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        [ImportingConstructor]
        public AccountSelectorViewModel(IAccountService accountService, ICacheService cacheService, IApplicationState applicationState,
            IPrinterService printerService)
        {
            _accounts = new ObservableCollection<AccountRowData>();
            _accountService = accountService;
            _cacheService = cacheService;
            _applicationState = applicationState;
            _printerService = printerService;
            ShowAccountDetailsCommand = new CaptionCommand<string>(Resources.AccountDetails.Replace(' ', '\r'), OnShowAccountDetails, CanShowAccountDetails);
            PrintCommand = new CaptionCommand<string>(Resources.Print, OnPrint);
            AccountButtonSelectedCommand = new CaptionCommand<AccountScreen>("", OnAccountScreenSelected);

            EventServiceFactory.EventService.GetEvent<GenericEvent<EventAggregator>>().Subscribe(
            x =>
            {
                if (x.Topic == EventTopicNames.ResetCache)
                {
                    _accountButtons = null;
                    _batchDocumentButtons = null;
                    _selectedAccountScreen = null;
                }
            });
        }

        private IEnumerable<DocumentTypeButtonViewModel> _batchDocumentButtons;
        public IEnumerable<DocumentTypeButtonViewModel> BatchDocumentButtons
        {
            get
            {
                return _batchDocumentButtons ??
                    (_batchDocumentButtons =
                    _selectedAccountScreen != null
                    ? _applicationState.GetBatchDocumentTypes(_selectedAccountScreen.AccountScreenValues.Select(x => x.AccountTypeName))
                            .Where(x => !string.IsNullOrEmpty(x.ButtonHeader))
                            .Select(x => new DocumentTypeButtonViewModel(x, null)) : null);
            }
        }

        private void OnAccountScreenSelected(AccountScreen accountScreen)
        {
            UpdateAccountScreen(accountScreen);
        }

        private string GetCurrencyFormat(int currencyId)
        {
            return _cacheService.GetCurrencySymbol(currencyId);
        }

        private void UpdateAccountScreen(AccountScreen accountScreen)
        {
            if (accountScreen == null) return;
            _batchDocumentButtons = null;
            _selectedAccountScreen = accountScreen;
            _accounts.Clear();
            var rows = new List<AccountRowData>();

            var detailedTemplateNames = accountScreen.AccountScreenValues.Where(x => x.DisplayDetails).Select(x => x.AccountTypeId);
            _accountService.GetAccountBalances(detailedTemplateNames.ToList(), GetFilter()).ToList().ForEach(x => rows.Add(new AccountRowData(x.Key.Name, x.Value.Balance, x.Value.Exchange, x.Key.Id, GetCurrencyFormat(x.Key.ForeignCurrencyId), x.Key.AccountTypeId, GetGroupKey(accountScreen, x.Key.AccountTypeId))));

            var templateTotals = accountScreen.AccountScreenValues.Where(x => !x.DisplayDetails).Select(x => x.AccountTypeId);
            _accountService.GetAccountTypeBalances(templateTotals.ToList(), GetFilter()).ToList().ForEach(x => rows.Add(new AccountRowData(x.Key.Name, x.Value.Balance, x.Value.Exchange, 0, "", x.Key.Id, GetGroupKey(accountScreen, x.Key.Id))));

            var hideIfZeroBalanceTypeIds =
                accountScreen.AccountScreenValues.Where(x => x.HideZeroBalanceAccounts).Select(x => x.AccountTypeId).ToList();

            _accounts.AddRange(rows.Where(x => ShouldKeepAccount(x, hideIfZeroBalanceTypeIds))
                .OrderBy(x => GetSortOrder(accountScreen.AccountScreenValues, x.AccountTypeId))
                .ThenBy(x => x.Name).ToList());


            RaisePropertyChanged(() => BatchDocumentButtons);
            RaisePropertyChanged(() => AccountButtons);

            OnRefreshed();
        }

        private static string GetGroupKey(AccountScreen accountScreen, int accountTypeId)
        {
            if (!accountScreen.DisplayAsTree) return null;
            return accountScreen.AccountScreenValues.Single(x => x.AccountTypeId == accountTypeId).AccountTypeName;
        }

        private static bool ShouldKeepAccount(AccountRowData accountRowData, IList<int> hideIfZeroBalanceTypeIds)
        {
            return !hideIfZeroBalanceTypeIds.Contains(accountRowData.AccountTypeId) ||
                   (hideIfZeroBalanceTypeIds.Contains(accountRowData.AccountTypeId) && accountRowData.Balance != 0);
        }

        private int GetSortOrder(IEnumerable<AccountScreenValue> values, int accountTypeId)
        {
            return values.Single(x => x.AccountTypeId == accountTypeId).SortOrder;
        }

        public IEnumerable<AccountScreen> AccountScreens
        {
            get { return _cacheService.GetAccountScreens(); }
        }

        private IEnumerable<AccountButton> _accountButtons;
        public IEnumerable<AccountButton> AccountButtons
        {
            get { return _accountButtons ?? (_accountButtons = AccountScreens.Select(x => new AccountButton(x, _cacheService))); }
        }

        private readonly ObservableCollection<AccountRowData> _accounts;
        public ObservableCollection<AccountRowData> Accounts
        {
            get { return _accounts; }
        }

        public ICaptionCommand ShowAccountDetailsCommand { get; set; }
        public ICaptionCommand PrintCommand { get; set; }
        public ICaptionCommand AccountButtonSelectedCommand { get; set; }

        public AccountRowData SelectedAccount { get; set; }

        private Expression<Func<AccountTransactionValue, bool>> GetFilter()
        {
            if (_selectedAccountScreen == null || _selectedAccountScreen.Filter == 0) return null;
            //Resources.All, Resources.Month, Resources.Week, Resources.WorkPeriod
            if (_selectedAccountScreen.Filter == 1)
            {
                var date = DateTime.Now.MonthStart();
                return x => x.Date >= date;
            }
            if (_selectedAccountScreen.Filter == 3)
            {
                var date = _applicationState.CurrentWorkPeriod.StartDate;
                return x => x.Date >= date;
            }
            return null;
        }

        private bool CanShowAccountDetails(string arg)
        {
            return SelectedAccount != null && SelectedAccount.AccountId > 0;
        }

        private void OnShowAccountDetails(object obj)
        {
            CommonEventPublisher.PublishEntityOperation(new AccountData(SelectedAccount.AccountId), EventTopicNames.DisplayAccountTransactions, EventTopicNames.ActivateAccountSelector);
        }

        public void Refresh()
        {
            UpdateAccountScreen(_selectedAccountScreen ?? (_selectedAccountScreen = AccountScreens.FirstOrDefault()));
        }

        private void OnPrint(string obj)
        {
            var report = new SimpleReport("");
            report.AddParagraph("Header");
            report.AddParagraphLine("Header", string.Format(_selectedAccountScreen.Name), true);
            report.AddParagraphLine("Header", "");

            report.AddColumnLength("Transactions", "60*", "40*");
            report.AddColumTextAlignment("Transactions", TextAlignment.Left, TextAlignment.Right);
            report.AddTable("Transactions", string.Format(Resources.Name_f, Resources.Account), Resources.Balance);

            foreach (var ad in Accounts)
            {
                report.AddRow("Transactions", ad.Name, ad.BalanceStr);
            }

            _printerService.PrintReport(report.Document, _applicationState.GetReportPrinter());
        }
    }
}
