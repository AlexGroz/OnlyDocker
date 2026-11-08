# Глава 1. Установка Docker тремя способами и полное удаление

> **Цель главы:** поставить Docker тремя разными способами — snap, пакет `docker.io` из
> Ubuntu, официальный репозиторий Docker. Каждый раз проверить, что он реально работает,
> и **полностью** удалить. В конце остаётся один способ — тот, которым пользуются в проде.
>
> **Нужно из прошлых глав:** глава 0 (проверка окружения).
> **Время:** 30–40 минут. **Права:** `sudo` (пароль спросят несколько раз).

---

## 1.0. Возврат в начало (сделай сам, без подсказок)

Перед новой темой восстанови состояние после главы 0:

1. Убедись, что Docker не установлен: ни бинарника `docker`, ни пакетов, ни systemd-юнитов.
2. Убедись, что на машине cgroup v2, есть `sudo` и свободны порты 8080/5432/5672/15672.
3. Восстанови рабочий каталог `~/docker-lab/reports` и сохрани в него отчёт об окружении.

<details>
<summary>Показать команды (сверься только после того, как попробовал сам)</summary>

```bash
command -v docker || echo "docker не установлен"
dpkg -l | grep -E '^ii +(docker|containerd)' || echo "пакетов docker нет"
systemctl list-unit-files | grep docker || echo "юнитов docker нет"

stat -fc %T /sys/fs/cgroup
sudo -v && echo "sudo ок"
ss -ltn | grep -E ':(8080|5432|5672|15672)\b' || echo "порты свободны"

mkdir -p ~/docker-lab/reports
{ echo "== отчёт окружения $(date -Is) =="; lsb_release -ds; uname -r; dpkg --print-architecture
  echo "cgroup: $(stat -fc %T /sys/fs/cgroup)"; echo "CPU: $(nproc)"; free -h | sed -n 2p; df -h / | tail -1
} | tee ~/docker-lab/reports/env-report.txt
```

</details>

**Готов идти дальше, если:** `command -v docker` ничего не вывел, а `stat -fc %T /sys/fs/cgroup`
показал `cgroup2fs`.

---

## 1.1. Что такое Docker на самом деле

Docker — это не «виртуалка» и не один бинарник, а связка процессов:

| Компонент | Что делает |
|---|---|
| `docker` (CLI) | тонкий клиент: превращает `docker run` в HTTP-запрос к демону |
| `dockerd` | демон: слушает unix-сокет `/var/run/docker.sock`, управляет образами, сетями, томами |
| `containerd` | менеджер жизненного цикла контейнеров (его же использует Kubernetes) |
| `runc` | низкоуровневый рантайм: создаёт namespaces и cgroups и запускает процесс |

Цепочка вызова: `docker run` → сокет → `dockerd` → `containerd` → `runc` → процесс.

**Что Docker даёт:** упаковку приложения со всеми зависимостями (образ), изоляцию
(namespaces: pid, net, mount, uts, ipc, user) и ограничение ресурсов (cgroups: cpu, memory),
одинаковое поведение на ноутбуке и на сервере.

**Чего Docker не даёт:** это не виртуализация — ядро общее, поэтому нет «своего» ядра в
контейнере. И это не оркестратор: нет самовосстановления, балансировки, автомасштабирования
и раскатки без простоя. Всё это — уровень Kubernetes, до него дойдём в главе 10.

---

## 1.2. Три способа установки и почему они не уживаются

| Способ | Пакет | Кто собирает | Версия | Плагины `buildx` / `compose` | Обновления |
|---|---|---|---|---|---|
| 1. snap | `docker` (snap) | Canonical, а **не** Docker Inc. | `29.8.0` (stable на 06.11.2026) | внутри snap | автоматически |
| 2. репозиторий Ubuntu | `docker.io` | Ubuntu | `29.1.3` | отдельными пакетами `docker-buildx`, `docker-compose-v2` | вместе с системой |
| 3. репозиторий Docker | `docker-ce` + `containerd.io` + плагины | Docker, Inc. | самая свежая | в комплекте | когда ты сам решишь |

Все три претендуют на одно и то же: бинарник `/usr/bin/docker`, сокет
`/var/run/docker.sock`, systemd-юнит `docker.service` и каталог данных. Пакеты конфликтуют,
а если поставить «в обход» — два демона начнут драться за сокет.

Поэтому правило «делаем все способы» здесь работает так: **поставили → проверили →
полностью удалили → следующий способ**. Порядок — от самого простого и наименее пригодного
для прода к самому правильному, чтобы в конце осталось то, с чем пойдём дальше.

> **В проде:** официальный репозиторий Docker + фиксация версии
> (`sudo apt-mark hold docker-ce`), чтобы ночное обновление не уронило ноды.
## 1.3. Способ 1 из 3: snap-пакет

```bash
sudo snap install docker
```

**Что произошло:** snap скачал самодостаточный пакет (`docker` 29.8.0, ~154 МБ) и поднял
systemd-сервисы. Важно понимать: этот snap собирает **Canonical**, а не Docker, Inc. —
в описании пакета прямо написано «not endorsed or published by Docker, Inc.».

**Проверка 1 — пакет и сервисы:**

```bash
snap list docker && snap services docker
```

**Ожидаемо:** строка `docker 29.8.0 …` и два сервиса в статусе `active`.

**Проверка 2 — клиент и демон отвечают:**

```bash
docker version --format 'CLI {{.Client.Version}} / server {{.Server.Version}}'
```

**Проверка 3 — где snap держит данные:**

```bash
docker info --format 'root={{.DockerRootDir}} driver={{.Driver}} cgroup={{.CgroupVersion}}'
ls -l /var/run/docker.sock
```

**Ожидаемо:** путь данных вида `/var/snap/docker/common/var-lib-docker` — не `/var/lib/docker`,
как у обычного пакета. Вот это и есть главная особенность snap-версии: у неё своё дерево
файлов и свои ограничения доступа к хосту.

**Проверка 4 — контейнер реально работает:**

```bash
sudo docker run --rm hello-world
```

Если `docker ps` под своим пользователем отвечает `permission denied`, добавь себя в группу:
`sudo adduser $USER docker`, затем перелогинься.

### Откат способа 1

```bash
sudo snap remove --purge docker
```

**Проверка отката:**

```bash
snap list docker ; ls /var/snap/docker 2>/dev/null ; command -v docker || echo "docker удалён"
```

**Ожидаемо:** `error: no matching snaps installed`, каталога нет, `docker удалён`.

---

## 1.4. Способ 2 из 3: пакет `docker.io` из репозитория Ubuntu

```bash
sudo apt update && sudo apt install -y docker.io docker-buildx docker-compose-v2
```

**Что произошло:** поставились `docker.io` (демон + CLI), зависимости `containerd`, `runc`,
и **отдельно** — плагины `buildx` и `compose` (в этом пакете они не входят в комплект,
в отличие от официального).

**Проверка 1 — что именно установилось:**

```bash
dpkg -l | grep -E '^ii +(docker|containerd|runc)' | awk '{print $2, $3}'
```

**Проверка 2 — сервис поднялся и включён в автозапуск:**

```bash
systemctl is-active docker; systemctl is-enabled docker
```

**Ожидаемо:** `active` и `enabled`.

**Проверка 3 — версии и плагины:**

```bash
docker version --format 'server {{.Server.Version}}'; docker compose version; docker buildx version
```

**Ожидаемо:** сервер `29.1.3` — ровно то, что предлагал `apt-cache policy docker.io` в главе 0.
Если `docker compose version` ругается — значит плагин не встал, проверь
`dpkg -l docker-compose-v2`.

**Проверка 4 — контейнер:**

```bash
sudo docker run --rm hello-world
```

**Проверка 5 — откуда взялась версия:**

```bash
apt-cache policy docker.io | sed -n '1,4p'
```

> **В проде:** этот способ плох тем, что версию Docker определяет Ubuntu, и обновление
> системы может внезапно обновить демон. Зато минимум действий и нет сторонних репозиториев —
> иногда это требование безопасности (только доверенные источники).

### Откат способа 2

```bash
sudo apt purge -y docker.io docker-buildx docker-compose-v2 containerd runc
sudo rm -rf /var/lib/docker /var/lib/containerd /etc/docker
sudo apt autoremove -y --purge
```

**Проверка отката:**

```bash
command -v docker || echo "CLI удалён"
systemctl is-active docker || echo "сервис неактивен"
ls /var/lib/docker /var/lib/containerd 2>/dev/null || echo "каталоги данных удалены"
dpkg -l | grep -cE '^ii +(docker|containerd|runc)'
```

## 1.5. Способ 3 из 3: официальный репозиторий Docker (его и оставляем)

Здесь пять маленьких шагов, каждый — со своей проверкой. Именно так это выглядит в
Ansible-роли или в README любого вменяемого проекта.

### Шаг 1. Зависимости для работы с репозиторием

```bash
sudo apt update && sudo apt install -y ca-certificates curl gnupg
```

**Проверка:**

```bash
dpkg -l ca-certificates curl gnupg 2>/dev/null | grep -c '^ii'
```

**Ожидаемо:** `3` — все три пакета стоят.

### Шаг 2. Каталог для ключей

```bash
sudo install -m 0755 -d /etc/apt/keyrings
```

**Зачем:** современный Debian/Ubuntu требует, чтобы сторонние ключи лежали в
`/etc/apt/keyrings` и были привязаны к конкретному репозиторию (`signed-by=`) — а не
сваливались в общий `trusted.gpg`. Иначе один скомпрометированный ключ подписывает
пакеты из любого источника.

**Проверка:**

```bash
ls -ld /etc/apt/keyrings
```

**Ожидаемо:** `drwxr-xr-x … root root … /etc/apt/keyrings`.

### Шаг 3. GPG-ключ репозитория

```bash
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
```

**Что произошло:** `gpg --dearmor` превратил текстовый ключ в бинарный формат, который
понимает `apt` (раньше он шёл с расширением `.asc` — тоже валидно).

**Проверка:**

```bash
ls -l /etc/apt/keyrings/docker.gpg
sudo gpg --no-default-keyring --keyring /etc/apt/keyrings/docker.gpg --list-keys | head -6
```

**Ожидаемо:** файл размером ~3 КБ и в выводе `gpg` — ключ Docker (`Docker Release (CE deb) …`).

### Шаг 4. Подключение самого репозитория

```bash
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update
```

**Что произошло:** добавлен источник `…/linux/ubuntu resolute stable`. Обрати внимание: ветка и
архитектура подставляются из системы — на Ubuntu 24.04 получилось бы `noble`, на ARM — `arm64`.

**Проверка 1 — файл источника:**

```bash
cat /etc/apt/sources.list.d/docker.list
```

**Проверка 2 — самый важный тест: `apt` теперь знает про `docker-ce`:**

```bash
apt-cache policy docker-ce | sed -n '1,6p'
```

**Ожидаемо:** `Candidate: 29.x.y-1~ubuntu…` и в таблице версий строка с
`https://download.docker.com/linux/ubuntu`. До главы 1 здесь было `(none)` — сравни с главой 0.

### Шаг 5. Установка пакетов Docker

```bash
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

**Что именно ставим:** `docker-ce` — демон, `docker-ce-cli` — клиент, `containerd.io` —
рантайм (отдельный, свежий), `docker-buildx-plugin` — сборка образов, `docker-compose-plugin` —
`docker compose`.

**Проверка 1 — версия сервера (сравни с 29.1.3 у `docker.io`):**

```bash
docker version --format 'CLI {{.Client.Version}} / server {{.Server.Version}} '
docker version --format 'API {{.Server.APIVersion}} / Go {{.Server.GoVersion}}'
```

**Проверка 2 — сервис:**

```bash
systemctl is-active docker; systemctl is-enabled docker; systemctl status docker --no-pager | head -5
```

**Проверка 3 — что демон о себе думает:**

```bash
docker info --format 'root={{.DockerRootDir}} driver={{.Driver}} cgroup={{.CgroupVersion}} runtime={{.DefaultRuntime}}'
```

**Ожидаемо:** `root=/var/lib/docker`, `driver=overlay2`, `cgroup=2`, `runtime=runc`.

**Проверка 4 — контейнер запускается:**

```bash
sudo docker run --rm hello-world
```

**Проверка 5 — плагины:**

```bash
docker compose version && docker buildx version
```

**Проверка 6 — журнал демона (пригодится при любой проблеме):**

```bash
journalctl -u docker --no-pager -n 10
```

### Шаг 6. Работа без `sudo`

```bash
sudo usermod -aG docker "$USER"
newgrp docker
```

**Что произошло:** пользователь добавлен в группу `docker`; `newgrp docker` переоткрывает
сессию с новой группой (альтернатива — выйти из терминала и зайти снова).

**Проверка:**

```bash
id -nG | tr ' ' '\n' | grep -x docker && docker ps
```

**Ожидаемо:** вывелось `docker`, а `docker ps` показал пустую таблицу **без** `sudo`.
Если всё ещё `permission denied` — группа не применилась к текущей сессии.

## 1.6. Настройка демона «как в проде»

Свежеустановленный Docker пишет логи контейнеров в файлы **без ограничения размера**.
Контейнер, который «немного» логирует, за месяц съедает диск — классический прод-инцидент.
Исправляется это настройкой демона.

```bash
sudo tee /etc/docker/daemon.json > /dev/null <<'JSON'
{
  "log-driver": "json-file",
  "log-opts": {
    "max-size": "10m",
    "max-file": "3"
  },
  "live-restore": true
}
JSON
sudo systemctl restart docker
```

**Что здесь:** `max-size`/`max-file` — ротация логов (не больше 3 файлов по 10 МБ на контейнер);
`live-restore` — контейнеры продолжают работать, пока демон перезапускается (обновление Docker
без простоя приложений).

**Проверка 1 — файл валиден как JSON (сломанный `daemon.json` = Docker не стартует):**

```bash
sudo jq . /etc/docker/daemon.json > /dev/null && echo "JSON валиден"
```

**Проверка 2 — демон принял настройки:**

```bash
docker info --format 'log-driver={{.LoggingDriver}} live-restore={{.LiveRestoreEnabled}}'
systemctl is-active docker
```

**Ожидаемо:** `log-driver=json-file live-restore=true` и `active`.

**Проверка 3 — настройка применилась к новым контейнерам:**

```bash
docker run -d --name lab-logcheck nginx:stable-alpine > /dev/null
docker inspect -f '{{json .HostConfig.LogConfig}}' lab-logcheck
docker rm -f lab-logcheck
```

**Ожидаемо:** в JSON видно `"Type":"json-file"` и опции `max-size`/`max-file`.

**Проверка 4 — что демон всё ещё полностью работоспособен:**

```bash
docker run --rm hello-world | head -3
docker compose version
```

> **В проде:** рядом с `daemon.json` обычно живут `data-root` (перенос данных на отдельный диск)
> и `default-address-pools` (когда стандартная подсеть `172.17.0.0/16` конфликтует с корпоративной
> сетью — частая причина «контейнер не видит базу»). А сам пакет фиксируют:
> `sudo apt-mark hold docker-ce docker-ce-cli containerd.io`.

---

## 1.7. Что осталось после трёх способов

| Способ | Ставили | Проверили | Удалили | Итог |
|---|---|---|---|---|
| snap | `snap install docker` | `snap services`, `docker version` | `snap remove --purge` | удалён |
| репозиторий Ubuntu | `apt install docker.io` | `docker.io` 29.1.3 | `apt purge` + `rm -rf /var/lib/docker` | удалён |
| официальный | ключ + репозиторий + 5 пакетов | `docker version/info/compose/buildx` | — | **оставлен** |

```bash
docker version --format 'Docker {{.Server.Version}} готов к работе'
```

---

## 1.8. Чек-лист самопроверки (глава 1)

| Вопрос | Ответ командой |
|---|---|
| Какая версия демона и CLI? | `docker version` |
| Демон включён в автозапуск? | `systemctl is-enabled docker` |
| Где Docker хранит данные? | `docker info --format '{{.DockerRootDir}}'` |
| Какой драйвер хранения и cgroup? | `docker info --format '{{.Driver}} {{.CgroupVersion}}'` |
| Логи ротируются? | `docker info --format '{{.LiveRestoreEnabled}}'`, `jq . /etc/docker/daemon.json` |
| Ты в группе docker? | `id -nG \| tr ' ' '\n' \| grep -x docker` |
| Откуда взят пакет? | `apt-cache policy docker-ce` |
| Работают плагины? | `docker compose version; docker buildx version` |
---

## 1.9. Откат главы 1: снести Docker полностью

**Внимание:** `/var/lib/docker` — это все образы, контейнеры и тома. Удаление каталога = удаление
данных. В лабе так и надо, в проде — никогда без бэкапа.

```bash
# 1. остановить сервисы
sudo systemctl stop docker docker.socket containerd 2>/dev/null

# 2. снять пакеты
sudo apt purge -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin docker-ce-rootless-extras

# 3. удалить данные и конфиги
sudo rm -rf /var/lib/docker /var/lib/containerd /etc/docker

# 4. убрать репозиторий и ключ
sudo rm -f /etc/apt/sources.list.d/docker.list /etc/apt/keyrings/docker.gpg

# 5. убрать себя из группы docker
sudo gpasswd -d "$USER" docker

# 6. подчистить зависимости и обновить индексы
sudo apt autoremove -y --purge
sudo apt update
```

**Проверка отката — пять независимых подтверждений:**

```bash
command -v docker || echo "1) CLI отсутствует"
systemctl status docker 2>&1 | head -2
ls /var/lib/docker /etc/docker 2>/dev/null || echo "2) данных и конфигов нет"
apt-cache policy docker-ce | sed -n '1,2p'
id -nG | tr ' ' '\n' | grep -x docker || echo "3) не в группе docker"
docker ps 2>&1 | head -1
```

**Ожидаемо:**

1. `1) CLI отсутствует`
2. `Unit docker.service could not be found.`
3. `2) данных и конфигов нет`
4. `docker-ce:` + `Installed: (none)` + `Candidate: (none)` — репозиторий отключён
5. `3) не в группе docker`
6. `bash: docker: command not found`

Если группа `docker` осталась пустой и мешает: `sudo groupdel docker`.

Всё. Машина в том же состоянии, что и в конце главы 0. В главе 2 ты поставишь Docker снова —
уже по памяти.

---

## 1.10. Шпаргалка главы

```bash
# --- snap ---
sudo snap install docker
snap list docker; snap services docker
sudo snap remove --purge docker

# --- docker.io из Ubuntu ---
sudo apt install -y docker.io docker-buildx docker-compose-v2
sudo apt purge -y docker.io docker-buildx docker-compose-v2 containerd runc

# --- официальный репозиторий ---
sudo apt install -y ca-certificates curl gnupg
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update
apt-cache policy docker-ce
sudo apt install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# --- после установки ---
sudo usermod -aG docker "$USER"; newgrp docker
sudo jq . /etc/docker/daemon.json
sudo systemctl restart docker

# --- проверки ---
docker version; docker info; docker run --rm hello-world
docker compose version; docker buildx version
systemctl is-active docker; systemctl is-enabled docker
journalctl -u docker -n 20 --no-pager

# --- полное удаление ---
sudo systemctl stop docker docker.socket containerd
sudo apt purge -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo rm -rf /var/lib/docker /var/lib/containerd /etc/docker
sudo rm -f /etc/apt/sources.list.d/docker.list /etc/apt/keyrings/docker.gpg
sudo gpasswd -d "$USER" docker
```

---

## 1.11. Типичные ошибки

| Симптом | Причина | Лечение |
|---|---|---|
| `E: The repository '… docker …' does not have a Release file` | неверное кодовое имя или архитектура в `docker.list` | сверить с `lsb_release -cs` и `dpkg --print-architecture` |
| `NO_PUBKEY …` при `apt update` | ключ не скачан или лежит не в `/etc/apt/keyrings` | повторить шаг 3, проверить `signed-by=` в `docker.list` |
| `docker: command not found` сразу после установки | устаревший кэш путей в shell | `hash -r` или новый терминал |
| `permission denied while trying to connect to the Docker daemon socket` | не применилась группа `docker` | `newgrp docker` или перелогин |
| `Job for docker.service failed …` после правки `daemon.json` | сломанный JSON | `sudo jq . /etc/docker/daemon.json`, `journalctl -u docker -n 50` |
| `dpkg: error … conflicts with docker.io` | остатки другого способа установки | сначала `apt purge` предыдущего пакета |
| `docker info` показывает `root=/var/snap/docker/...` | работает snap-версия | `sudo snap remove --purge docker`, затем ставить заново |
| `Cannot connect to the Docker daemon at unix:///var/run/docker.sock` | демон остановлен | `sudo systemctl start docker` |

---

**Итог главы 1:** ты умеешь ставить Docker тремя способами, проверять его работоспособность
шестью разными командами и полностью удалять без остатков. Остался официальный репозиторий —
с ним и работаем дальше.




