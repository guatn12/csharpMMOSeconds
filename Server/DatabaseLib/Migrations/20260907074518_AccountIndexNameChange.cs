using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DatabaseLib.Migrations
{
    /// <inheritdoc />
    public partial class AccountIndexNameChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "idx_accounts_normalized_login_id",
                table: "accounts",
                newName: "ux_accounts_normalized_login_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "ux_accounts_normalized_login_id",
                table: "accounts",
                newName: "idx_accounts_normalized_login_id");
        }
    }
}
