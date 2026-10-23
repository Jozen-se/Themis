using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 2a: the shape of a setting definition follows its backing (D-63, RR-99). value_type becomes
/// optional, and a check holds Setting and Column definitions to a value type and Data and Planned definitions to no
/// value type, default, minimum, maximum or allowed values. No row is rewritten: development and test databases are
/// recreated for this step. No grant changes. Every statement is literal text, fixed once merged (RR-39).
/// </summary>
internal sealed partial class SettingDefinitionShape : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "value_type",
            schema: "kernel",
            table: "setting_definition",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20);

        migrationBuilder.AddCheckConstraint(
            name: "ck_setting_definition_shape",
            schema: "kernel",
            table: "setting_definition",
            sql: "(backing IN ('Setting', 'Column') AND value_type IS NOT NULL) OR (backing IN ('Data', 'Planned') AND value_type IS NULL AND default_value IS NULL AND min IS NULL AND max IS NULL AND allowed_values IS NULL)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_setting_definition_shape",
            schema: "kernel",
            table: "setting_definition");

        migrationBuilder.AlterColumn<string>(
            name: "value_type",
            schema: "kernel",
            table: "setting_definition",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true);
    }
}
