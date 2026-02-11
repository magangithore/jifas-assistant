using System;
using System.Collections.Generic;
using System.Linq;

namespace Jifas.Assistant.Services
{
    /// <summary>
    /// Service to provide JIFAS domain context and understanding
    /// Understands JIFAS workflows, modules, roles, and business logic
    /// </summary>
    public interface IJifasContextService
    {
        /// <summary>
        /// Get JIFAS workflow stages for a given module/transaction
        /// </summary>
        List<WorkflowStage> GetWorkflowStages(string module);

        /// <summary>
        /// Get roles that can perform an action in a workflow
        /// </summary>
        List<string> GetAllowedRolesForAction(string module, string action);

        /// <summary>
        /// Get what user role should do next in a workflow
        /// </summary>
        string GetNextWorkflowStep(string module, string currentStatus, string userRole);

        /// <summary>
        /// Validate if user can perform action based on role
        /// </summary>
        bool CanUserPerformAction(string module, string action, string userRole);

        /// <summary>
        /// Get required fields for creating a transaction
        /// </summary>
        List<string> GetRequiredFields(string module, string action);

        /// <summary>
        /// Get business rules for a module
        /// </summary>
        List<string> GetBusinessRules(string module);

        /// <summary>
        /// Get common issues/solutions for a module
        /// </summary>
        Dictionary<string, string> GetCommonIssues(string module);
    }

    public class WorkflowStage
    {
        public int StageNumber { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> AllowedRoles { get; set; } = new List<string>();
        public string Action { get; set; } = string.Empty;
    }

    /// <summary>
    /// Service providing JIFAS domain knowledge
    /// Contains workflows, permissions, business rules, and troubleshooting guides
    /// </summary>
    public class JifasContextService : IJifasContextService
    {
        private readonly ILoggerService _logger;

        // Define JIFAS workflows
        private readonly Dictionary<string, List<WorkflowStage>> _workflows =
            new Dictionary<string, List<WorkflowStage>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Invoice",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Draft",
                            Description = "Initial creation of invoice with PD Form",
                            AllowedRoles = new List<string> { "Pemohon Invoice", "Requestor" },
                            Action = "Create Invoice"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Head Approval",
                            Description = "Head Department approves or rejects invoice",
                            AllowedRoles = new List<string> { "Head Department" },
                            Action = "Approve/Reject"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Need Check Correction",
                            Description = "Invoice needs correction",
                            AllowedRoles = new List<string> { "Pemohon Invoice" },
                            Action = "Apply Correction"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 4,
                            Status = "Finance PD Approval",
                            Description = "Finance verifies and approves PD",
                            AllowedRoles = new List<string> { "Finance", "Finance Head" },
                            Action = "Verify & Approve"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 5,
                            Status = "Need Finance Checking",
                            Description = "Finance Head performs final checking",
                            AllowedRoles = new List<string> { "Finance Head" },
                            Action = "Check"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 6,
                            Status = "Ready Payment",
                            Description = "Invoice ready for payment processing",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create Payment"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 7,
                            Status = "Accounting Checking",
                            Description = "Accounting verifies before posting",
                            AllowedRoles = new List<string> { "Accounting" },
                            Action = "Check & Post"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 8,
                            Status = "Posted",
                            Description = "Invoice posted to journal",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View"
                        }
                    }
                },
                {
                    "PUM",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Draft",
                            Description = "Create PUM (Pengajuan Uang Muka)",
                            AllowedRoles = new List<string> { "Pemohon PUM", "Requestor" },
                            Action = "Create PUM"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Head Approval",
                            Description = "Head Department approves or rejects",
                            AllowedRoles = new List<string> { "Head Department" },
                            Action = "Approve/Reject"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Finance Approval",
                            Description = "Finance approves and generates UM number",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Approve"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 4,
                            Status = "Ready Payment",
                            Description = "PUM ready for payment",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create Payment"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 5,
                            Status = "Payment Created",
                            Description = "Payment has been created",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "View Payment"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 6,
                            Status = "Realization",
                            Description = "Requestor creates realization for PUM",
                            AllowedRoles = new List<string> { "Pemohon PUM" },
                            Action = "Create Realization"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 7,
                            Status = "Finance Verification",
                            Description = "Finance verifies realization",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Verify"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 8,
                            Status = "Accounting Checking",
                            Description = "Accounting performs final check",
                            AllowedRoles = new List<string> { "Accounting" },
                            Action = "Check & Post"
                        }
                    }
                },
                {
                    "Payment",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Draft",
                            Description = "Create payment with method selection",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create Payment"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Accounting Checking",
                            Description = "Accounting verifies payment",
                            AllowedRoles = new List<string> { "Accounting" },
                            Action = "Check"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Approved",
                            Description = "Payment approved for processing",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Process"
                        }
                    }
                },
                {
                    "Receiving",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Draft",
                            Description = "Create receiving with invoice reference",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create Receiving"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Tax Approval",
                            Description = "Tax department approves tax documents",
                            AllowedRoles = new List<string> { "Tax" },
                            Action = "Approve"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Finance Checking",
                            Description = "Finance verifies receiving",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Check"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 4,
                            Status = "Accounting Checking",
                            Description = "Accounting final check and posting",
                            AllowedRoles = new List<string> { "Accounting" },
                            Action = "Check & Post"
                        }
                    }
                },
                {
                    "CashBank",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Draft",
                            Description = "Create cash/bank transaction",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Finance Checking",
                            Description = "Finance verifies transaction",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Check"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Accounting Checking",
                            Description = "Accounting final check and posting",
                            AllowedRoles = new List<string> { "Accounting" },
                            Action = "Check & Post"
                        }
                    }
                },
                {
                    "MasterCompany",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Create",
                            Description = "Create new company/branch/division",
                            AllowedRoles = new List<string> { "IT Admin" },
                            Action = "Create Company"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Edit",
                            Description = "Edit company details",
                            AllowedRoles = new List<string> { "IT Admin" },
                            Action = "Edit Company"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Active",
                            Description = "Company is active and ready to use",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View"
                        }
                    }
                },
                {
                    "MasterDivision",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Create",
                            Description = "Create new division",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create Division"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Edit",
                            Description = "Edit division details",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Edit Division"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Active",
                            Description = "Division is active",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View"
                        }
                    }
                },
                {
                    "MasterDepartment",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Create",
                            Description = "Create new department",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create Department"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Edit",
                            Description = "Edit department details",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Edit Department"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Active",
                            Description = "Department is active",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View"
                        }
                    }
                },
                {
                    "MasterVendor",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Create",
                            Description = "Create new vendor/supplier",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Create Vendor"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Edit",
                            Description = "Edit vendor details",
                            AllowedRoles = new List<string> { "Finance" },
                            Action = "Edit Vendor"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Active",
                            Description = "Vendor is active",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View"
                        }
                    }
                },
                {
                    "MasterCOA",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Create",
                            Description = "Create new Chart of Accounts",
                            AllowedRoles = new List<string> { "IT Admin", "Accounting HO" },
                            Action = "Create COA"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Expand",
                            Description = "Expand COA with child accounts",
                            AllowedRoles = new List<string> { "IT Admin", "Accounting HO" },
                            Action = "Expand COA"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Edit",
                            Description = "Edit COA details (number cannot change)",
                            AllowedRoles = new List<string> { "IT Admin", "Accounting HO" },
                            Action = "Edit COA"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 4,
                            Status = "Remove",
                            Description = "Remove COA (only if not used in transactions)",
                            AllowedRoles = new List<string> { "IT Admin", "Accounting HO" },
                            Action = "Remove COA"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 5,
                            Status = "Active",
                            Description = "COA is active and ready to use",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View"
                        }
                    }
                },
                {
                    "MasterPeriod",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Create",
                            Description = "Create new accounting period",
                            AllowedRoles = new List<string> { "IT Admin", "Accounting HO" },
                            Action = "Create Period"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Open",
                            Description = "Period is open for transactions",
                            AllowedRoles = new List<string> { "All" },
                            Action = "Post Transactions"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Closed",
                            Description = "Close period - no more transaction posting",
                            AllowedRoles = new List<string> { "IT Admin", "Accounting HO" },
                            Action = "Close Period"
                        }
                    }
                },
                {
                    "Budget",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Upload",
                            Description = "Upload budget from Excel file",
                            AllowedRoles = new List<string> { "Finance", "IT Admin" },
                            Action = "Upload Budget"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Create",
                            Description = "Manually create budget with monthly allocation",
                            AllowedRoles = new List<string> { "Finance", "IT Admin" },
                            Action = "Create Budget"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Active",
                            Description = "Budget is active and ready for transactions",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View Budget"
                        }
                    }
                },
                {
                    "Reports",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "Journal",
                            Description = "View journal transactions detail",
                            AllowedRoles = new List<string> { "Finance", "Accounting", "Finance Head" },
                            Action = "View Journal"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "Ledger",
                            Description = "View account ledger summary",
                            AllowedRoles = new List<string> { "Finance", "Accounting", "Finance Head" },
                            Action = "View Ledger"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "TrialBalance",
                            Description = "View trial balance - all accounts with saldo",
                            AllowedRoles = new List<string> { "Accounting", "Finance Head" },
                            Action = "View Trial Balance"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 4,
                            Status = "BalanceSheet",
                            Description = "View balance sheet (Assets = Liabilities + Equity)",
                            AllowedRoles = new List<string> { "Finance", "Accounting", "Finance Head" },
                            Action = "View Balance Sheet"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 5,
                            Status = "ProfitLoss",
                            Description = "View profit & loss statement",
                            AllowedRoles = new List<string> { "Finance", "Accounting", "Finance Head" },
                            Action = "View P&L"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 6,
                            Status = "CashFlow",
                            Description = "View cash flow statement",
                            AllowedRoles = new List<string> { "Finance", "Finance Head" },
                            Action = "View Cash Flow"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 7,
                            Status = "Aging",
                            Description = "View customer aging report (outstanding payments)",
                            AllowedRoles = new List<string> { "Finance", "Finance Head" },
                            Action = "View Aging"
                        }
                    }
                },
                {
                    "ReportSetup",
                    new List<WorkflowStage>
                    {
                        new WorkflowStage
                        {
                            StageNumber = 1,
                            Status = "MapCOA",
                            Description = "Map COA to report rows for report generation",
                            AllowedRoles = new List<string> { "Accounting", "IT Admin" },
                            Action = "Map COA to Report"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 2,
                            Status = "RemoveMapping",
                            Description = "Remove COA mapping from report",
                            AllowedRoles = new List<string> { "Accounting", "IT Admin" },
                            Action = "Remove Mapping"
                        },
                        new WorkflowStage
                        {
                            StageNumber = 3,
                            Status = "Active",
                            Description = "Report setup is complete and ready",
                            AllowedRoles = new List<string> { "All" },
                            Action = "View Report"
                        }
                    }
                }
            };

        // Define role-action permissions
        private readonly Dictionary<string, List<string>> _rolePermissions =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Head Department",
                    new List<string> { "Create Invoice", "Approve/Reject", "View Reports", "Check Budget" }
                },
                {
                    "Finance",
                    new List<string> { 
                        "Create Invoice", "Create PUM", "Create Payment", "Create Receiving", "Create Cash Bank",
                        "Approve PUM", "Verify Realization", "Check Status", "View Reports", "Bulk Posting"
                    }
                },
                {
                    "Finance Head",
                    new List<string> { 
                        "Approve Invoice", "Check Invoice", "Check Payment", "View Reports", 
                        "Bulk Posting", "Period Management"
                    }
                },
                {
                    "Accounting",
                    new List<string> { 
                        "Check Invoice", "Check Payment", "Check Receiving", "Check Cash Bank",
                        "Journal Transaction", "Journal Memorial", "Reverse Journal", "Bulk Posting", "View Reports"
                    }
                },
                {
                    "Tax",
                    new List<string> { 
                        "Tax Approval Invoice", "Tax Approval PUM", "Tax Approval Receiving", 
                        "Tax Approval Cash Bank", "View Tax Reports"
                    }
                },
                {
                    "IT Admin",
                    new List<string> { 
                        "Master Company", "Master Division", "Master Department", "Master Vendor", 
                        "Master COA", "Master Budget", "User Management", "Role Management"
                    }
                },
                {
                    "Pemohon Invoice",
                    new List<string> { 
                        "Create Invoice", "Apply Correction", "View Status"
                    }
                },
                {
                    "Pemohon PUM",
                    new List<string> { 
                        "Create PUM", "Create Realization", "View Status", "Check PUM Budget"
                    }
                },
                {
                    "Accounting HO",
                    new List<string> { 
                        "Master COA", "Master Period", "Report Setup", "Journal Memorial", 
                        "Reverse Journal", "View Reports", "Bulk Posting"
                    }
                },
                {
                    "Requestor",
                    new List<string> { 
                        "Create Invoice", "Create PUM", "View Status", "View Budget"
                    }
                }
            };

        // Define required fields for actions
        private readonly Dictionary<string, List<string>> _requiredFields =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Invoice",
                    new List<string> { "PD Form (Mandatory)", "Budget", "Tax/PPH (if applicable)", "Documents (if any)" }
                },
                {
                    "PUM",
                    new List<string> { "Requestor", "Amount", "Description", "Budget Allocation" }
                },
                {
                    "Payment",
                    new List<string> { "Payment Method", "Invoice/PUM Selection", "Amount", "Allocation" }
                },
                {
                    "Receiving",
                    new List<string> { "Invoice Reference (for IK)", "Amount", "Tax Documents (PPN/PPH)" }
                },
                {
                    "CashBank",
                    new List<string> { "Type (Payment/Receiving)", "Amount", "Bank/Cash Account", "Description" }
                },
                {
                    "Realization",
                    new List<string> { "Vendor", "COA", "Tax", "Amount" }
                },
                {
                    "MasterCompany",
                    new List<string> { "Company Name", "Code", "Address" }
                },
                {
                    "MasterDivision",
                    new List<string> { "Division Name", "Company", "Code" }
                },
                {
                    "MasterDepartment",
                    new List<string> { "Department Name", "Division", "Code" }
                },
                {
                    "MasterVendor",
                    new List<string> { "Vendor Name", "Code", "Contact Person", "Address", "Phone" }
                },
                {
                    "MasterCOA",
                    new List<string> { "COA Number", "COA Name", "Type (Asset/Liability/Equity/Revenue/Expense)" }
                },
                {
                    "MasterPeriod",
                    new List<string> { "Period", "Year", "Start Date", "End Date" }
                },
                {
                    "Budget",
                    new List<string> { "Company", "Period", "Department", "Total Amount", "Monthly Allocation" }
                }
            };

        // Define business rules
        private readonly Dictionary<string, List<string>> _businessRules =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Invoice",
                    new List<string>
                    {
                        "PD Form is mandatory for all invoices",
                        "Invoice must be approved by Head Department before Finance approval",
                        "Over budget invoices require Head approval checklist",
                        "Invoice number is auto-generated after Finance approval",
                        "Tax documents (PPN/PPH) must be verified before posting",
                        "Correction can only be done when status is 'Need Check Correction'"
                    }
                },
                {
                    "PUM",
                    new List<string>
                    {
                        "Budget status: OK, CM (Cross Month), CA (Cross Accumulation), CY (Cross Year - cannot proceed)",
                        "CY (Cross Year) status prevents PUM from continuing",
                        "UM number is auto-generated after Finance approval",
                        "Realization must cover all PUM amount",
                        "Excess/shortage automatically goes to Cash Bank",
                        "Realization requires vendor, COA, tax, and amount details"
                    }
                },
                {
                    "Payment",
                    new List<string>
                    {
                        "Multiple payment methods can be selected",
                        "Payment must reference Invoice or PUM",
                        "Allocation must specify distribution to accounts",
                        "Status flow: Draft ? Accounting Checking ? Approved"
                    }
                },
                {
                    "Receiving",
                    new List<string>
                    {
                        "Receiving IK (Invoice Billing) requires invoice reference",
                        "Receiving Non-IK is manual entry without invoice",
                        "Tax documents (PPN/PPH) must be uploaded before Tax approval",
                        "Excess/shortage must be documented"
                    }
                },
                {
                    "Budget",
                    new List<string>
                    {
                        "Monthly budget total must equal annual total",
                        "Budget can be uploaded from Excel or created manually",
                        "Budget is per company per period"
                    }
                },
                {
                    "Void",
                    new List<string>
                    {
                        "Applies to all transaction types",
                        "Removed: Status before posting",
                        "Void: Status after reverse journal",
                        "Void transactions are reversed and cannot be recovered"
                    }
                },
                {
                    "MasterCompany",
                    new List<string>
                    {
                        "Each company must have unique code",
                        "Company address is mandatory",
                        "Company can have multiple divisions"
                    }
                },
                {
                    "MasterCOA",
                    new List<string>
                    {
                        "COA number cannot be changed after creation",
                        "COA can be deleted only if no transactions use it",
                        "COA can have child accounts (breakdowns)",
                        "Balance sheet COA: Assets, Liabilities, Equity",
                        "P&L COA: Revenue, Cost, Expense"
                    }
                },
                {
                    "MasterPeriod",
                    new List<string>
                    {
                        "One accounting period per company per year",
                        "Period must be opened before posting transactions",
                        "Period can be closed to prevent new postings",
                        "Reports are generated based on open period"
                    }
                },
                {
                    "BudgetRules",
                    new List<string>
                    {
                        "Monthly budget total must equal annual total",
                        "Budget is per company per department per period",
                        "Transactions cannot exceed budget allocation (status: CM, CA, CY)",
                        "Budget can be uploaded from Excel template"
                    }
                },
                {
                    "Reports",
                    new List<string>
                    {
                        "Journal: Shows transaction detail with debit/credit",
                        "Ledger: Shows account summary with opening/closing balance",
                        "Trial Balance: Verification that system is balanced (total debit = credit)",
                        "Balance Sheet: Financial position (Assets = Liabilities + Equity)",
                        "P&L: Profit/Loss from operations (Revenue - Expense = Profit)",
                        "Cash Flow: Movement of cash in/out",
                        "Aging: Outstanding customer payments by age (30/60/90 days)"
                    }
                }
            };

        // Define common issues and solutions
        private readonly Dictionary<string, Dictionary<string, string>> _commonIssues =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                {
                    "Invoice",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "Cannot create invoice - PD Form missing",
                            "PD Form is mandatory for all invoices. You must fill in the PD Form first before proceeding."
                        },
                        {
                            "Invoice rejected by head",
                            "Check the rejection reason from Head Department and make necessary corrections. Then resubmit for approval."
                        },
                        {
                            "Over budget warning",
                            "Your invoice amount exceeds the allocated budget. Head Department must check the approval box before this invoice can proceed."
                        },
                        {
                            "Cannot change invoice - already posting",
                            "Invoice numbers are auto-generated after Finance approval. You can only make changes before Finance approval. Contact IT if urgent changes are needed."
                        }
                    }
                },
                {
                    "PUM",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "PUM rejected - Cross Year budget",
                            "Your PUM exceeds the annual budget allocation (CY status). You cannot proceed. Please adjust the amount or contact Finance."
                        },
                        {
                            "Cannot create realization - PUM not approved",
                            "Wait for Finance approval and UM number generation first before creating realization."
                        },
                        {
                            "Realization amount mismatch",
                            "Realization total must match the PUM amount. If there is excess or shortage, it will be automatically transferred to Cash Bank."
                        }
                    }
                },
                {
                    "Payment",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "Payment cannot be created - no approved invoice",
                            "Payment requires an approved invoice or PUM. Make sure the source document is already approved before creating payment."
                        },
                        {
                            "Cannot proceed payment - accounting not checked",
                            "Accounting department must verify the payment first. Status will change to 'Approved' after Accounting checking."
                        }
                    }
                },
                {
                    "Login",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "Cannot login with Windows account",
                            "Use Windows login WITHOUT '@jababeka.com' domain. Example: if your email is john.doe@jababeka.com, login with 'john.doe'"
                        },
                        {
                            "Login failed - password incorrect",
                            "JIFAS uses Windows authentication. If you cannot login, check your Windows login credentials or contact IT Help Desk."
                        },
                        {
                            "Session expired",
                            "Your session has timed out. Please login again by entering your Windows username (without @jababeka.com)."
                        }
                    }
                },
                {
                    "MasterData",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "Cannot delete COA - transaction exists",
                            "This COA has been used in transactions and cannot be deleted. Contact IT if you need to remove it."
                        },
                        {
                            "COA number format invalid",
                            "COA numbers must follow the format: [Type][SubType][Detail] (e.g., 1-1001-00). Check your number format."
                        },
                        {
                            "Company code duplicate",
                            "This company code already exists. Please use a different code."
                        },
                        {
                            "Period already closed",
                            "This accounting period is closed. You cannot post transactions to a closed period. Contact Accounting."
                        }
                    }
                },
                {
                    "BudgetIssues",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "Budget file upload failed",
                            "Ensure your Excel file matches the required template with columns: Company, Department, Monthly1-12, Total"
                        },
                        {
                            "Monthly budget not equal to total",
                            "Sum of monthly budget (Jan-Dec) must equal the total annual budget. Check your calculation."
                        },
                        {
                            "No budget found - transaction rejected",
                            "Transactions cannot proceed without approved budget. Contact Finance to create budget first."
                        },
                        {
                            "Over budget - Cross Year (CY) status",
                            "This transaction exceeds annual budget. Contact Finance for approval."
                        }
                    }
                },
                {
                    "ReportIssues",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {
                            "No data in report",
                            "The selected period may not have transactions yet, or the period is not open. Check period status."
                        },
                        {
                            "Trial balance not balanced",
                            "Total debit does not equal total credit. This indicates posting errors. Contact Accounting."
                        },
                        {
                            "Cannot generate report - period not closed",
                            "Close the period first before generating final reports."
                        }
                    }
                }
            };

        public JifasContextService(ILoggerService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public List<WorkflowStage> GetWorkflowStages(string module)
        {
            if (_workflows.TryGetValue(module, out var stages))
            {
                return stages;
            }
            return new List<WorkflowStage>();
        }

        public List<string> GetAllowedRolesForAction(string module, string action)
        {
            var workflow = GetWorkflowStages(module);
            var stage = workflow.FirstOrDefault(s => s.Action.Equals(action, StringComparison.OrdinalIgnoreCase));
            return stage?.AllowedRoles ?? new List<string>();
        }

        public string GetNextWorkflowStep(string module, string currentStatus, string userRole)
        {
            var workflow = GetWorkflowStages(module);
            var currentStage = workflow.FirstOrDefault(s => s.Status.Equals(currentStatus, StringComparison.OrdinalIgnoreCase));
            
            if (currentStage == null) 
                return "Unknown";

            var nextStage = workflow.FirstOrDefault(s => s.StageNumber == currentStage.StageNumber + 1);
            if (nextStage == null) 
                return "Completed";

            if (nextStage.AllowedRoles.Contains(userRole) || nextStage.AllowedRoles.Contains("All"))
            {
                return $"{nextStage.Status}: {nextStage.Description}";
            }

            return $"Next step requires {string.Join(" or ", nextStage.AllowedRoles)}";
        }

        public bool CanUserPerformAction(string module, string action, string userRole)
        {
            if (!_rolePermissions.TryGetValue(userRole, out var permissions))
            {
                return false;
            }

            return permissions.Any(p => p.Equals(action, StringComparison.OrdinalIgnoreCase));
        }

        public List<string> GetRequiredFields(string module, string action)
        {
            if (_requiredFields.TryGetValue(module, out var fields))
            {
                return fields;
            }
            return new List<string>();
        }

        public List<string> GetBusinessRules(string module)
        {
            if (_businessRules.TryGetValue(module, out var rules))
            {
                return rules;
            }
            return new List<string>();
        }

        public Dictionary<string, string> GetCommonIssues(string module)
        {
            if (_commonIssues.TryGetValue(module, out var issues))
            {
                return issues;
            }
            return new Dictionary<string, string>();
        }
    }
}
