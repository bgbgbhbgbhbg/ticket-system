# 1. 停掉並刪除 container + 這個 compose 專案定義的 volume(-v 是關鍵,會把 pgdata 這個 named volume 一起刪掉)
docker compose down -v --remove-orphans

# 2. 確認 volume 真的被刪乾淨了(下面這行如果有輸出,代表還沒刪乾淨)
docker volume ls | grep ticket

# 3. 如果上一步還看得到殘留的 volume,手動指名刪除(volume 名稱通常是「資料夾名_pgdata」)
docker volume rm ticket-system_pgdata

# 4. 把 docker-compose.yml 換成上面這份新版(image: postgres:18 + volume 路徑改成 /var/lib/postgresql)

# 5. 重新啟動,這次是全新的 volume + 全新的目錄結構
docker compose up -d

# 6. 確認乾淨啟動,沒有再跳出那個警告
docker compose logs postgres | grep -i "unused mount"
# 沒有任何輸出,代表這次是乾淨的、沒有殘留舊資料

# 7. 確認版本正確
docker exec -it ticket-postgres psql -U ticket_admin -d ticket_booking -c "SELECT version();"

# 8. 重新套用 migration
cd backend
dotnet ef database update \
  --project TicketBooking.Infrastructure \
  --startup-project TicketBooking.Api