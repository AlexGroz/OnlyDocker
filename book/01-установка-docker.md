# Глава 1. Установка Docker тремя способами и полное удаление

> [← Оглавление книги](00-план.md) · [← Глава 0](00-окружение.md) · [Глава 2 →](02-образы-и-контейнеры.md)

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

**Ключи:** здесь те же команды, что в главе 0, и каждый ключ там разобран — вернись к 0.1–0.10,
если хоть один вызывает вопрос. Новых ключей в этом блоке нет.

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
> snap — это десктоп и быстрые тесты. `docker.io` — когда нужен минимум движений
> и версия не критична (например, CI-раннер внутри той же ОС).

---

## 1.3. Способ 1 из 3: snap-пакет

```bash
sudo snap install docker
```

**Ключи:** `sudo` — выполнить от имени root (установка пакетов требует прав);
`snap install <имя>` — поставить snap-пакет из Snap Store, канал по умолчанию `latest/stable`;
здесь **нет** `--classic`, потому что Docker в Snap Store поставляется в изолированном режиме
со своими ограничениями — это и есть источник отличий от обычного пакета.

**Что произошло:** snap скачал самодостаточный пакет (`docker` 29.8.0, ~154 МБ) и поднял
systemd-сервисы. Важно понимать: этот snap собирает **Canonical**, а не Docker, Inc. —
в описании пакета прямо написано «not endorsed or published by Docker, Inc.».

**Проверка 1 — пакет и сервисы:**

```bash
snap list docker && snap services docker
```

**Ключи:** `snap list <имя>` — установленные snap-пакеты (версия, ревизия, канал, заметки);
`snap services <имя>` — systemd-сервисы, которые создал snap (у Docker их два: демон и
вспомогательный), и их состояние.

**Ожидаемо:** строка `docker 29.8.0 …` и два сервиса в статусе `active`.

**Проверка 2 — клиент и демон отвечают:**

```bash
docker version --format 'CLI {{.Client.Version}} / server {{.Server.Version}}'
```

**Ключи:** `--format` (он же `-f`) принимает **шаблон Go**: текст внутри `{{ }}` — это поле из
ответа демона. `.Client` — твой CLI, `.Server` — демон, `.Version` — номер версии. Тот же приём
работает во всех командах Docker: `docker inspect -f '{{.State.Status}}' <имя>`.

**Проверка 3 — где snap держит данные:**

```bash
docker info --format 'root={{.DockerRootDir}} driver={{.Driver}} cgroup={{.CgroupVersion}}'
ls -l /var/run/docker.sock
```

**Ключи:** `docker info` — состояние демона целиком; `--format` — вывести только нужные поля:
`.DockerRootDir` — каталог данных, `.Driver` — драйвер хранилища слоёв, `.CgroupVersion` — версия
cgroup. `ls -l` — длинный формат, чтобы увидеть права и владельца сокета: сокет принадлежит
`root:docker`, и именно поэтому обычный пользователь получает `permission denied`.

**Ожидаемо:** путь данных вида `/var/snap/docker/common/var-lib-docker` — не `/var/lib/docker`,
как у обычного пакета. Вот это и есть главная особенность snap-версии: у неё своё дерево
файлов и свои ограничения доступа к хосту.

**Проверка 4 — контейнер реально работает:**

```bash
sudo docker run --rm hello-world
```

**Ключи:** `docker run <образ>` — создать контейнер из образа и запустить его;
`--rm` — удалить контейнер сразу после завершения (без него он останется в `docker ps -a`
со статусом `Exited`); `hello-world` — имя образа без тега, то есть `:latest`.

Если `docker ps` под своим пользователем отвечает `permission denied`, добавь себя в группу:
`sudo adduser $USER docker`, затем перелогинься.

### Откат способа 1

```bash
sudo snap remove --purge docker
```

**Ключи:** `snap remove <имя>` — удалить snap-пакет; `--purge` — удалить вместе с ним **данные и
снапшоты** (`/var/snap/docker/...`). Без `--purge` данные остались бы на диске, и следующий
эксперимент начался бы не с чистого листа.

**Проверка отката:**

```bash
snap list docker ; ls /var/snap/docker 2>/dev/null ; command -v docker || echo "docker удалён"
```

**Ключи:** `;` — выполнять команды по порядку, **невзирая** на результат предыдущей (в отличие от
`&&`); `ls /var/snap/docker` — проверить, остались ли данные; `2>/dev/null` — не показывать ошибку,
если каталога нет; `command -v … || echo` — подставить текст, если бинарника нет.

**Ожидаемо:** `error: no matching snaps installed`, каталога нет, `docker удалён`.

---

## 1.4. Способ 2 из 3: пакет `docker.io` из репозитория Ubuntu

```bash
sudo apt update && sudo apt install -y docker.io docker-buildx docker-compose-v2
```

**Ключи:** `apt update` — обновить **индексы** пакетов (список того, что доступно), сами пакеты
не трогает; `apt install` — установить перечисленные пакеты и их зависимости;
`-y` (`--assume-yes`) — не задавать вопрос «Продолжить? [Y/n]», нужно для скриптов.

**Что произошло:** поставились `docker.io` (демон + CLI), зависимости `containerd`, `runc`,
и **отдельно** — плагины `buildx` и `compose` (в этом пакете они не входят в комплект,
в отличие от официального).

**Проверка 1 — что именно установилось:**

```bash
dpkg -l | grep -E '^ii +(docker|containerd|runc)' | awk '{print $2, $3}'
```

**Ключи:** `dpkg -l` — список пакетов и их состояние; `^ii` в шаблоне — «установлен и настроен»
(первая `i` — желаемое состояние, вторая — текущее); `awk '{print $2, $3}'` — оставить только
2-ю и 3-ю колонки, то есть имя пакета и версию, без служебных флагов.

**Проверка 2 — сервис поднялся и включён в автозапуск:**

```bash
systemctl is-active docker; systemctl is-enabled docker
```

**Ключи:** `systemctl is-active <юнит>` — запущен ли сервис **сейчас** (печатает `active`/`inactive`,
а не всю портянку статуса); `is-enabled` — включён ли автозапуск при загрузке (`enabled`/`disabled`).

**Ожидаемо:** `active` и `enabled`.

**Проверка 3 — версии и плагины:**

```bash
docker version --format 'server {{.Server.Version}}'; docker compose version; docker buildx version
```

**Ключи:** `--format` — шаблон Go (см. выше), здесь печатаем только версию **демона**;
`docker compose version` и `docker buildx version` — не самостоятельные программы, а **плагины**
CLI; если плагина нет, Docker ответит `'compose' is not a docker command`.

**Ожидаемо:** сервер `29.1.3` — ровно то, что предлагал `apt-cache policy docker.io` в главе 0.
Если `docker compose version` ругается — значит плагин не встал, проверь
`dpkg -l docker-compose-v2`.

**Проверка 4 — контейнер:**

```bash
sudo docker run --rm hello-world
```

**Ключи:** те же, что выше: `run` — создать и запустить, `--rm` — удалить после выхода.

**Проверка 5 — откуда взялась версия:**

```bash
apt-cache policy docker.io | sed -n '1,4p'
```

**Ключи:** `apt-cache policy <пакет>` — установленная и доступная версии и их источник;
`sed -n '1,4p'` — показать только первые 4 строки (там уже есть всё нужное, дальше идёт длинная
таблица версий).

> **В проде:** этот способ плох тем, что версию Docker определяет Ubuntu, и обновление
> системы может внезапно обновить демон. Зато минимум действий и нет сторонних репозиториев —
> иногда это требование безопасности (только доверенные источники).

### Откат способа 2

```bash
sudo apt purge -y docker.io docker-buildx docker-compose-v2 containerd runc
sudo rm -rf /var/lib/docker /var/lib/containerd /etc/docker
sudo apt autoremove -y --purge
```

**Ключи:** `apt purge` — удалить пакет **вместе с конфигурационными файлами** (в отличие от
`apt remove`, который конфиги оставляет); `rm -rf` — снести каталоги данных (образы, контейнеры,
тома) — это необратимо; `apt autoremove --purge` — убрать зависимости, которые больше никому
не нужны, тоже вместе с конфигами.

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

**Ключи:** те же `apt update` и `-y`, что и выше; назначение пакетов: `ca-certificates` —
корневые сертификаты для HTTPS, `curl` — скачать ключ, `gnupg` (`gpg`) — преобразовать его в формат,
который понимает `apt`.

**Проверка:**

```bash
dpkg -l ca-certificates curl gnupg 2>/dev/null | grep -c '^ii'
```

**Ключи:** `dpkg -l <пакеты>` — состояние **конкретных** пакетов (а не всего списка);
`grep -c` — посчитать совпавшие строки: `3` значит все три установлены;
`2>/dev/null` — скрыть строку ошибки, если пакета нет вовсе.

**Ожидаемо:** `3` — все три пакета стоят.

### Шаг 2. Каталог для ключей

```bash
sudo install -m 0755 -d /etc/apt/keyrings
```

**Ключи:** `install` — «умный `mkdir` + `chmod`»: `-d` создать каталог, `-m 0755` сразу задать
права (владелец `rwx`, остальные `r-x`). Одной командой получаем каталог с нужными правами,
без отдельного `chmod`.

**Зачем:** современный Debian/Ubuntu требует, чтобы сторонние ключи лежали в
`/etc/apt/keyrings` и были привязаны к конкретному репозиторию (`signed-by=`) — а не
сваливались в общий `trusted.gpg`. Иначе один скомпрометированный ключ подписывает
пакеты из любого источника.

**Проверка:**

```bash
ls -ld /etc/apt/keyrings
```

**Ключи:** `-l` — длинный формат (права, владелец), `-d` (`--directory`) — показать **сам каталог**,
а не его содержимое. Без `-d` `ls` вывел бы файлы внутри.

**Ожидаемо:** `drwxr-xr-x … root root … /etc/apt/keyrings`.

### Шаг 3. GPG-ключ репозитория

```bash
curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg
```

**Ключи:** `curl -f` (`--fail`) — считать ошибкой HTTP-код ≥ 400 (иначе `curl` молча запишет
страницу ошибки вместо ключа); `-s` (`--silent`) — без прогресса; `-S` (`--show-error`) — но текст
ошибки показывать; `-L` (`--location`) — идти по редиректам. `gpg --dearmor` — превратить
текстовый («бронированный») ключ в бинарный формат `apt`; `-o файл` — куда писать результат.
`chmod a+r` — `a` (all, все) `+r` (добавить чтение): `apt` читает ключ не от root.

**Что произошло:** `gpg --dearmor` превратил текстовый ключ в бинарный формат, который
понимает `apt` (раньше он шёл с расширением `.asc` — тоже валидно).

**Проверка:**

```bash
ls -l /etc/apt/keyrings/docker.gpg
sudo gpg --no-default-keyring --keyring /etc/apt/keyrings/docker.gpg --list-keys | head -6
```

**Ключи:** `gpg --no-default-keyring` — не смотреть в личную связку ключей пользователя;
`--keyring <файл>` — работать именно с этим файлом; `--list-keys` — показать, какие ключи внутри;
`head -6` — первые шесть строк вывода.

**Ожидаемо:** файл размером ~3 КБ и в выводе `gpg` — ключ Docker (`Docker Release (CE deb) …`).

### Шаг 4. Подключение самого репозитория

```bash
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu $(lsb_release -cs) stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list
sudo apt update
```

**Ключи:** `echo` печатает строку, а `| sudo tee <файл>` записывает её в файл **от root**
(`tee` нужен потому, что `sudo echo … > файл` перенаправление выполняет твоя оболочка, а не `sudo`,
и она упрётся в права). `$(...)` — подстановка: `arch=…` и кодовое имя берутся из системы.
Внутри квадратных скобок — атрибуты источника: `arch=` — под какую архитектуру,
`signed-by=` — каким ключом проверять подпись; `stable` — ветка репозитория.

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

**Ключи:** `-y` — не спрашивать подтверждения (для скриптов и Ansible); `apt install` сам подтянет
зависимости, поэтому `runc` и часть библиотек ты не перечисляешь. Роли пяти пакетов — в таблице ниже.

**Что именно ставим:** `docker-ce` — демон, `docker-ce-cli` — клиент, `containerd.io` —
рантайм (отдельный, свежий), `docker-buildx-plugin` — сборка образов, `docker-compose-plugin` —
`docker compose`.

**Проверка 1 — версия сервера (сравни с 29.1.3 у `docker.io`):**

```bash
docker version --format 'CLI {{.Client.Version}} / server {{.Server.Version}} '
docker version --format 'API {{.Server.APIVersion}} / Go {{.Server.GoVersion}}'
```

**Ключи:** `--format` — шаблон Go; `.Client` — твой CLI, `.Server` — демон;
`.APIVersion` — версия HTTP API (важна для совместимости инструментов), `.GoVersion` — на какой
версии Go собран демон. Две команды — потому что в одном шаблоне удобно держать одну тему.

**Проверка 2 — сервис:**

```bash
systemctl is-active docker; systemctl is-enabled docker; systemctl status docker --no-pager | head -5
```

**Ключи:** `is-active` и `is-enabled` объяснены выше; `status` — полный статус юнита (последние
строчки лога, главный PID, cgroup); `--no-pager` — не открывать `less` (иначе вывод «залипнет»
в ожидании `q`); `head -5` — первые пять строк.

**Проверка 3 — что демон о себе думает:**

```bash
docker info --format 'root={{.DockerRootDir}} driver={{.Driver}} cgroup={{.CgroupVersion}} runtime={{.DefaultRuntime}}'
```

**Ключи:** `--format` с полями: `.DockerRootDir` — где лежат образы и тома, `.Driver` — драйвер
слоёв (`overlay2` — норма), `.CgroupVersion` — версия cgroup (ждём `2`), `.DefaultRuntime` — чем
запускаются контейнеры (`runc`).

**Ожидаемо:** `root=/var/lib/docker`, `driver=overlay2`, `cgroup=2`, `runtime=runc`.

**Проверка 4 — контейнер запускается:**

```bash
sudo docker run --rm hello-world
```

**Ключи:** `run` — создать и запустить контейнер; `--rm` — удалить его после выхода.

**Проверка 5 — плагины:**

```bash
docker compose version && docker buildx version
```

**Проверка 6 — журнал демона (пригодится при любой проблеме):**

```bash
journalctl -u docker --no-pager -n 10
```

**Ключи:** `-u docker` (`--unit`) — только записи этого systemd-юнита; `-n 10` (`--lines`)
последние 10 строк; `--no-pager` — без постраничного просмотра. Это первая команда при любом
«Docker не стартует»: там будет причина (`invalid JSON`, `failed to start daemon` и т.п.).

### Шаг 6. Работа без `sudo`

```bash
sudo usermod -aG docker "$USER"
newgrp docker
```

**Ключи:** `usermod -aG <группа> <пользователь>` — **добавить** (`-a`, `--append`) пользователя в
**дополнительные** группы (`-G`), не затирая остальные: без `-a` список групп был бы **перезаписан**
и ты потерял бы `sudo`. `newgrp docker` — переоткрыть оболочку с новой группой без выхода из системы.

**Что произошло:** пользователь добавлен в группу `docker`; `newgrp docker` переоткрывает
сессию с новой группой (альтернатива — выйти из терминала и зайти снова).

**Проверка:**

```bash
id -nG | tr ' ' '\n' | grep -x docker && docker ps
```

**Ключи:** `id -nG` — группы именами; `tr ' ' '\n'` — заменить пробелы переводами строк, чтобы
группы шли по одной в строке; `grep -x docker` (`--line-regexp`) — строка должна быть **ровно**
`docker` (иначе совпало бы что-то вроде `docker-compose`); `&&` — `docker ps` выполнится только если
группа найдена.

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

**Ключи:** `--format` с полями `.LoggingDriver` (какой драйвер логов выбран) и
`.LiveRestoreEnabled` (`true`/`false` — переживают ли контейнеры перезапуск демона);
`systemctl is-active` — живой ли демон после `restart`.

**Ожидаемо:** `log-driver=json-file live-restore=true` и `active`.

**Проверка 3 — настройка применилась к новым контейнерам:**

```bash
docker run -d --name lab-logcheck nginx:stable-alpine > /dev/null
docker inspect -f '{{json .HostConfig.LogConfig}}' lab-logcheck
docker rm -f lab-logcheck
```

**Ключи:** `docker run -d` (`--detach`) — запустить в фоне и сразу вернуть терминал;
`> /dev/null` — выбросить длинный ID контейнера, он нам не нужен;
`inspect -f '{{json …}}'` — вывести выбранное поле **в виде JSON** (так видно вложенные опции);
`docker rm -f` — удалить контейнер принудительно, даже работающий.

**Ожидаемо:** в JSON видно `"Type":"json-file"` и опции `max-size`/`max-file`.

**Проверка 4 — что демон всё ещё полностью работоспособен:**

```bash
docker run --rm hello-world | head -3
docker compose version
```

**Ключи:** `run --rm` — запустить и сразу удалить контейнер; `head -3` — первые три строки вывода
(проверяем, что демон вообще работает); `compose version` — что плагин на месте.

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

**Ключи:** `--format` — шаблон Go; `{{.Server.Version}}` — версия сервера, остальной текст в
шаблоне печатается как есть.

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

**Ключи:** `systemctl stop <юниты>` — остановить сейчас (автозапуск выключится только вместе с
пакетом); `apt purge` — удалить пакет вместе с конфигами; `rm -rf` — снести данные;
`rm -f` — удалить файлы без вопросов и без ошибки, если их уже нет; `gpasswd -d <пользователь>
<группа>` — **удалить** (`-d`) пользователя из группы; `apt autoremove --purge` — убрать ненужные
зависимости вместе с их конфигами; `apt update` — перечитать индексы уже без удалённого репозитория.

**Проверка отката — пять независимых подтверждений:**

```bash
command -v docker || echo "1) CLI отсутствует"
systemctl status docker 2>&1 | head -2
ls /var/lib/docker /etc/docker 2>/dev/null || echo "2) данных и конфигов нет"
apt-cache policy docker-ce | sed -n '1,2p'
id -nG | tr ' ' '\n' | grep -x docker || echo "3) не в группе docker"
docker ps 2>&1 | head -1
```

**Ключи:** `systemctl status docker 2>&1` — соединить поток ошибок с обычным выводом, иначе текст
`Unit … could not be found` (он идёт в stderr) не попадёт в `head`; `ls … 2>/dev/null` — не показывать
ошибку, если каталогов нет; `sed -n '1,2p'` — только две строки `apt-cache policy`;
`docker ps 2>&1 | head -1` — первая строка ошибки от Docker, которого уже нет.

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

Ключи здесь не разбираются — они разобраны в шагах выше. Шпаргалка нужна для быстрого
подсматривания, а не для первого знакомства.

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




