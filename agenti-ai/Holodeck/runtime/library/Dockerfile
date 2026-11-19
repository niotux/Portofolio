# The trusted image of every project/library environment. The supervisor runs as root and starts each declared
# service and agent as its own non-root user; workloads install anything else into their own homes, or system-wide
# through sudo when the user granted it to them.
FROM debian:bookworm-slim
LABEL holodeck.library-runtime="1"
RUN apt-get update \
 && apt-get install -y --no-install-recommends python3 python3-pip python3-venv python3-aiohttp procps sudo build-essential git curl ca-certificates nodejs npm ffmpeg \
 && rm -rf /var/lib/apt/lists/* \
 && pip install --break-system-packages --no-cache-dir uv==0.4.30 \
 && printf 'Set disable_coredump false\n' > /etc/sudo.conf \
 && mkdir -p /etc/holodeck /var/lib/holodeck /opt/holodeck-library \
 && chmod 700 /etc/holodeck
COPY supervisor.py launcher.py /opt/holodeck-library/
RUN chmod 755 /opt/holodeck-library && chmod 644 /opt/holodeck-library/*.py
WORKDIR /
ENTRYPOINT ["/usr/bin/python3", "-I", "-u", "/opt/holodeck-library/supervisor.py"]
