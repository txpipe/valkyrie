FROM ubuntu:22.04
WORKDIR /app
# Install wget
RUN apt-get update && apt-get install -y wget
# Download tar
RUN wget https://github.com/IntersectMBO/cardano-node/releases/download/8.7.3/cardano-node-8.7.3-linux.tar.gz
# Extract tar
RUN tar -xvf cardano-node-8.7.3-linux.tar.gz
# Download Config
RUN wget https://book.world.dev.cardano.org/environments/mainnet/submit-api-config.json
ENV NETWORK="--mainnet"
ENTRYPOINT ./cardano-submit-api --config submit-api-config.json $NETWORK --socket-path $CARDANO_NODE_SOCKET_PATH --listen-address 0.0.0.0