using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Themis.Kernel.Persistence.Migrations;

/// <summary>
/// Milestone 2 step 2a: drops ux_setting_definition_key, a unique index that repeated the alternate key
/// ak_setting_definition_key on kernel.setting_definition.key (RR-95). The alternate key stays the key's one unique
/// constraint. No grant changes. Every statement is literal text, fixed once merged (RR-39).
/// </summary>
internal sealed partial class DropDuplicateSettingKeyIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ux_setting_definition_key",
            schema: "kernel",
            table: "setting_definition");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ux_setting_definition_key",
            schema: "kernel",
            table: "setting_definition",
            column: "key",
            unique: true);
    }
}
