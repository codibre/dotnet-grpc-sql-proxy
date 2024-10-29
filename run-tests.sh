npm run docker:build
docker_id=$(docker run -d --network=host test-grpc-client)
npm run test:coverage
docker kill $docker_id