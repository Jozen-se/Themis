# Command catalogue

Generated from the catalogue attributes on each command (RR-07; milestone 2 step 4). Do not edit by hand: change the
attributes beside `[Command]` and regenerate with
`THEMIS_COMMANDS_RECORD=1 dotnet test --project tests/Themis.Architecture.Tests --filter-class Themis.Architecture.Tests.CommandCatalogueTests`.
The error codes are the command's own; every command also answers 401, 403, 409 for a concurrent duplicate,
422 `idempotency_key_reused` and, with If-Match, 428 from the pipeline (Build Brief, API conventions).

## ACCOUNT.BLOCK

| Field | Value |
|---|---|
| Command type | `BlockAccount` |
| Permission | `ACCOUNT.BLOCK` |
| If-Match | required |
| Rules | D-56, D-69, R-API-02, R-PP-05 |
| Audits | Account |
| Events | `ACCOUNT_BLOCKED` |
| Errors | `reason_required`, `reason_too_long`, `account_not_found`, `version_stale`, `transition_not_allowed`, `account_used_by_posting_rule` |

## ACCOUNT.CREATE

| Field | Value |
|---|---|
| Command type | `CreateAccount` |
| Permission | `ACCOUNT.CREATE` |
| If-Match | no |
| Rules | D-69, R-MD-05, RR-45 |
| Audits | Account |
| Events | `ACCOUNT_CREATED` |
| Errors | `chart_required`, `account_number_required`, `name_required`, `name_too_long`, `account_type_required`, `chart_of_accounts_unknown`, `account_number_duplicate` |

## ACCOUNT.DEACTIVATE

| Field | Value |
|---|---|
| Command type | `DeactivateAccount` |
| Permission | `ACCOUNT.DEACTIVATE` |
| If-Match | required |
| Rules | D-56, D-69, R-API-02, R-DOC-01, R-PP-05 |
| Audits | Account |
| Events | `ACCOUNT_DEACTIVATED` |
| Errors | `account_not_found`, `version_stale`, `transition_not_allowed`, `account_used_by_posting_rule`, `account_balance_not_zero` |

## ACCOUNT.REACTIVATE

| Field | Value |
|---|---|
| Command type | `ReactivateAccount` |
| Permission | `ACCOUNT.REACTIVATE` |
| If-Match | required |
| Rules | D-69, R-API-02 |
| Audits | Account |
| Events | `ACCOUNT_REACTIVATED` |
| Errors | `account_not_found`, `version_stale`, `transition_not_allowed` |

## ACCOUNT.UNBLOCK

| Field | Value |
|---|---|
| Command type | `UnblockAccount` |
| Permission | `ACCOUNT.UNBLOCK` |
| If-Match | required |
| Rules | D-69, R-API-02 |
| Audits | Account |
| Events | `ACCOUNT_UNBLOCKED` |
| Errors | `account_not_found`, `version_stale`, `transition_not_allowed` |

## ACCOUNT.UPDATE

| Field | Value |
|---|---|
| Command type | `UpdateAccount` |
| Permission | `ACCOUNT.UPDATE` |
| If-Match | required |
| Rules | D-69, R-API-02, R-MD-06, R-PP-04 |
| Audits | Account |
| Events | `ACCOUNT_UPDATED` |
| Errors | `name_required`, `name_too_long`, `account_type_required`, `account_not_found`, `version_stale`, `transition_not_allowed`, `account_number_fixed`, `account_chart_fixed`, `account_type_fixed`, `account_control_fixed` |

## CHART_OF_ACCOUNTS.CREATE

| Field | Value |
|---|---|
| Command type | `CreateChartOfAccounts` |
| Permission | `CHART_OF_ACCOUNTS.CREATE` |
| If-Match | no |
| Rules | R-MD-05, RR-45, RR-52 |
| Audits | ChartOfAccounts |
| Events | `CHART_OF_ACCOUNTS_CREATED` |
| Errors | `code_required`, `name_required`, `name_too_long`, `template_too_long`, `chart_of_accounts_code_duplicate` |

## CHART_OF_ACCOUNTS.UPDATE

| Field | Value |
|---|---|
| Command type | `UpdateChartOfAccounts` |
| Permission | `CHART_OF_ACCOUNTS.UPDATE` |
| If-Match | required |
| Rules | R-API-02, R-MD-06 |
| Audits | ChartOfAccounts |
| Events | `CHART_OF_ACCOUNTS_UPDATED` |
| Errors | `name_required`, `name_too_long`, `template_too_long`, `chart_of_accounts_not_found`, `version_stale`, `chart_of_accounts_code_fixed` |

## COMPANY.CREATE

| Field | Value |
|---|---|
| Command type | `CreateCompany` |
| Permission | `COMPANY.CREATE` |
| If-Match | no |
| Rules | D-64, D-65, R-MD-05, RR-45 |
| Audits | Party, Company |
| Events | `PARTY_CREATED`, `COMPANY_CREATED` |
| Errors | `code_required`, `legal_name_required`, `legal_name_too_long`, `organisation_number_required`, `organisation_number_too_long`, `vat_number_too_long`, `time_zone_required`, `time_zone_invalid`, `country_code_required`, `base_currency_required`, `fiscal_year_start_month_out_of_range`, `chart_of_accounts_required`, `tax_rounding_level_required`, `base_currency_unknown`, `chart_of_accounts_unknown`, `company_code_duplicate`, `organisation_number_duplicate`, `number_series_missing` |

## COMPANY.UPDATE

| Field | Value |
|---|---|
| Command type | `UpdateCompany` |
| Permission | `COMPANY.UPDATE` |
| If-Match | required |
| Rules | D-64, D-65, R-API-02, R-MD-05, R-MD-06, RR-130 |
| Audits | Company, Party |
| Events | `COMPANY_UPDATED`, `PARTY_UPDATED` |
| Errors | `legal_name_required`, `legal_name_too_long`, `organisation_number_required`, `organisation_number_too_long`, `vat_number_too_long`, `time_zone_required`, `time_zone_invalid`, `country_code_required`, `base_currency_required`, `fiscal_year_start_month_out_of_range`, `chart_of_accounts_required`, `tax_rounding_level_required`, `rounding_mode_required`, `company_not_found`, `version_stale`, `base_currency_unknown`, `chart_of_accounts_unknown`, `organisation_number_duplicate`, `company_code_fixed`, `company_base_currency_fixed`, `company_time_zone_fixed`, `company_country_code_fixed`, `company_organisation_number_fixed`, `company_rounding_mode_fixed`, `company_chart_of_accounts_fixed`, `company_fiscal_year_start_month_fixed` |

## INVENTORY_ADJUSTMENT.CREATE

| Field | Value |
|---|---|
| Command type | `CreateInventoryAdjustment` |
| Permission | `INVENTORY_ADJUSTMENT.CREATE` |
| If-Match | no |
| Rules | R-DOC-01, R-INV-02, D-40 |
| Audits | Document, DocumentLine, InventoryAdjustment |
| Events | `INVENTORY_ADJUSTMENT_CREATED` |
| Errors | `company_required`, `external_reference_too_long`, `line_number_not_positive`, `line_number_duplicate`, `line_item_required`, `line_quantity_zero`, `line_entered_unit_cost_negative`, `line_entered_unit_cost_precision`, `value_out_of_range`, `line_limit_exceeded`, `company_unknown`, `warehouse_unknown`, `warehouse_other_company`, `line_item_unknown`, `line_warehouse_required`, `line_warehouse_unknown`, `line_warehouse_other_company`, `quantity_precision` |
| Transition | INVENTORY_ADJUSTMENT: Draft to Draft |

## INVENTORY_ADJUSTMENT.DISCARD

| Field | Value |
|---|---|
| Command type | `DiscardInventoryAdjustment` |
| Permission | `INVENTORY_ADJUSTMENT.DISCARD` |
| If-Match | required |
| Rules | R-DOC-01, R-DOC-06, R-API-02 |
| Audits | Document |
| Events | `INVENTORY_ADJUSTMENT_DISCARDED` |
| Errors | `inventory_adjustment_not_found`, `version_stale`, `transition_not_allowed` |
| Transition | INVENTORY_ADJUSTMENT: Draft to Discarded |

## INVENTORY_ADJUSTMENT.POST

| Field | Value |
|---|---|
| Command type | `PostInventoryAdjustment` |
| Permission | `INVENTORY_ADJUSTMENT.POST` |
| If-Match | required |
| Rules | R-LED-05, R-LED-08, R-INV-03, R-INV-05, R-PP-02, R-LED-04, R-DOC-07, D-69 |
| Audits | Document, DocumentLine |
| Events | `INVENTORY_ADJUSTED` |
| Errors | `inventory_adjustment_not_found`, `version_stale`, `transition_not_allowed`, `no_open_lines`, `reason_code_required`, `item_not_stocked`, `item_not_active`, `lot_required`, `posting_date_required`, `fiscal_period_not_open`, `entered_unit_cost_on_outbound`, `document_currency_not_base_currency`, `stock_insufficient`, `posting_rule_missing`, `posting_rule_account_not_active`, `amount_out_of_range`, `number_series_missing` |
| Transition | INVENTORY_ADJUSTMENT: Draft to Posted |

## INVENTORY_ADJUSTMENT.UPDATE

| Field | Value |
|---|---|
| Command type | `UpdateInventoryAdjustment` |
| Permission | `INVENTORY_ADJUSTMENT.UPDATE` |
| If-Match | required |
| Rules | R-DOC-02, R-API-02, R-INV-02 |
| Audits | Document, DocumentLine, InventoryAdjustment |
| Events | `INVENTORY_ADJUSTMENT_UPDATED` |
| Errors | `external_reference_too_long`, `line_number_not_positive`, `line_number_duplicate`, `line_item_required`, `line_quantity_zero`, `line_entered_unit_cost_negative`, `line_entered_unit_cost_precision`, `value_out_of_range`, `line_limit_exceeded`, `inventory_adjustment_not_found`, `version_stale`, `document_not_draft`, `warehouse_unknown`, `warehouse_other_company`, `line_item_unknown`, `line_warehouse_required`, `line_warehouse_unknown`, `line_warehouse_other_company`, `quantity_precision` |
| Transition | INVENTORY_ADJUSTMENT: Draft to Draft |

## ITEM.CREATE

| Field | Value |
|---|---|
| Command type | `CreateItem` |
| Permission | `ITEM.CREATE` |
| If-Match | no |
| Rules | R-MD-05, R-INV-02, RR-45 |
| Audits | Item, ItemCompany |
| Events | `ITEM_CREATED`, `ITEM_COMPANY_CREATED` |
| Errors | `company_required`, `description_required`, `description_too_long`, `net_weight_kg_negative`, `net_weight_kg_precision`, `sales_multiple_not_positive`, `min_order_qty_not_positive`, `value_out_of_range`, `base_unit_unknown`, `item_posting_group_unknown`, `default_warehouse_unknown`, `default_warehouse_other_company`, `item_number_duplicate`, `quantity_precision` |

## SETTING.CLEAR

| Field | Value |
|---|---|
| Command type | `ClearSetting` |
| Permission | `SETTING.CLEAR` |
| If-Match | required |
| Rules | R-SET-01, D-63, R-API-02 |
| Audits | SettingValue |
| Events | `SETTING_CHANGED` |
| Errors | `setting_key_invalid`, `setting_target_invalid`, `setting_unknown`, `setting_not_settable`, `setting_locked`, `setting_level_not_allowed`, `valid_from_required`, `valid_from_not_allowed`, `version_stale`, `company_unknown`, `site_unknown`, `site_other_company`, `warehouse_unknown`, `warehouse_other_company`, `setting_value_absent` |

## SETTING.SET

| Field | Value |
|---|---|
| Command type | `SetSetting` |
| Permission | `SETTING.SET` |
| If-Match | required |
| Rules | R-SET-01, D-63, R-API-02 |
| Audits | SettingValue |
| Events | `SETTING_CHANGED` |
| Errors | `setting_key_invalid`, `setting_target_invalid`, `setting_value_required`, `setting_unknown`, `setting_not_settable`, `setting_locked`, `setting_level_not_allowed`, `valid_from_required`, `valid_from_not_allowed`, `version_stale`, `company_unknown`, `site_unknown`, `site_other_company`, `warehouse_unknown`, `warehouse_other_company` |

## SITE.CREATE

| Field | Value |
|---|---|
| Command type | `CreateSite` |
| Permission | `SITE.CREATE` |
| If-Match | no |
| Rules | R-MD-05, R-SEC-01 |
| Audits | Site |
| Events | `SITE_CREATED` |
| Errors | `company_required`, `code_required`, `name_required`, `name_too_long`, `time_zone_invalid`, `company_unknown`, `site_code_duplicate` |

## SITE.UPDATE

| Field | Value |
|---|---|
| Command type | `UpdateSite` |
| Permission | `SITE.UPDATE` |
| If-Match | required |
| Rules | R-API-02, R-MD-06 |
| Audits | Site |
| Events | `SITE_UPDATED` |
| Errors | `name_required`, `name_too_long`, `time_zone_invalid`, `site_not_found`, `version_stale`, `site_code_fixed` |

## WAREHOUSE.CREATE

| Field | Value |
|---|---|
| Command type | `CreateWarehouse` |
| Permission | `WAREHOUSE.CREATE` |
| If-Match | no |
| Rules | R-MD-05, R-SEC-01, RR-133 |
| Audits | Warehouse |
| Events | `WAREHOUSE_CREATED`, `WAREHOUSE_UPDATED` |
| Errors | `company_required`, `site_required`, `code_required`, `name_required`, `name_too_long`, `site_unknown`, `site_other_company`, `warehouse_code_duplicate`, `warehouse_default_taken` |

## WAREHOUSE.UPDATE

| Field | Value |
|---|---|
| Command type | `UpdateWarehouse` |
| Permission | `WAREHOUSE.UPDATE` |
| If-Match | required |
| Rules | R-API-02, R-MD-06, R-DOC-01 |
| Audits | Warehouse |
| Events | `WAREHOUSE_UPDATED` |
| Errors | `name_required`, `name_too_long`, `warehouse_not_found`, `site_not_found`, `version_stale`, `warehouse_code_fixed`, `warehouse_not_active`, `warehouse_default_taken` |
