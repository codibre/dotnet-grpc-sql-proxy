docker compose up -d

# Wait till the TCP port becomes available.
pathToWaitForItInContainer="./bin/wait-for-it.sh" 
echo "Copy 'wait-for-it.sh' into the container '$pathToWaitForItInContainer'"
docker cp "wait-for-it.sh" mssql_server:"$pathToWaitForItInContainer"
echo "Wait for the TCP port to become available (or 30s timeout)"
# Note that we gotta add '--user root' for the `chmod` command
docker exec -i --user root mssql_server sh -c "chmod +x $pathToWaitForItInContainer && $pathToWaitForItInContainer localhost:1433 -t 30"
echo "Wait 1s extra as even after the port is available, the server still may need a moment"
sleep 1

createDbSqlScriptInContainer="/home/CreateAndMigrateDataContext.sql"
dbName="SampleDb"

echo "Copy 'PrepareDb.Sql' into the container"
docker cp "PrepareDb.Sql" mssql_server:"$createDbSqlScriptInContainer"

echo "Create new database"
docker exec -i mssql_server /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Sa12345!" -d master -Q "CREATE DATABASE $dbName"
echo "Create schema in DB"
docker exec -i mssql_server /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P "Sa12345!" -d $dbName -i "$createDbSqlScriptInContainer"