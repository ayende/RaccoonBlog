#!/bin/bash
set -euo pipefail

APP_IMAGE_LOCAL="ayendeblog-website:latest"

TEST_REGION="eu-central-1"
TEST_REGISTRY="846865426872.dkr.ecr.eu-central-1.amazonaws.com"
TEST_REPOSITORY="ravendb/ayendeblog-website-test"
TEST_ECR="${TEST_REGISTRY}/${TEST_REPOSITORY}:latest"

PROD_REGION="us-west-2"
PROD_REGISTRY="438865404129.dkr.ecr.us-west-2.amazonaws.com"
PROD_REPOSITORY="ayende-com"
PROD_ECR="${PROD_REGISTRY}/${PROD_REPOSITORY}:latest"

if [[ "${PRODUCTION:-false}" == "true" ]]; then
  AWS_REGION="${PROD_REGION}"
  TARGET_REGISTRY="${PROD_REGISTRY}"
  TARGET_ECR="${PROD_ECR}"
  ENV_NAME="PROD"
else
  AWS_REGION="${TEST_REGION}"
  TARGET_REGISTRY="${TEST_REGISTRY}"
  TARGET_ECR="${TEST_ECR}"
  ENV_NAME="TEST"
fi

echo "Build Docker image"
docker build --pull --progress=plain -f docker/Dockerfile -t "${APP_IMAGE_LOCAL}" .

echo "Login into Docker repo - ${ENV_NAME}"
aws ecr get-login-password --region "${AWS_REGION}" \
  | docker login --username AWS --password-stdin "${TARGET_REGISTRY}"

echo "Tag image -> ${TARGET_ECR}"
docker tag "${APP_IMAGE_LOCAL}" "${TARGET_ECR}"

echo "Push image -> ${TARGET_ECR}"
docker push "${TARGET_ECR}"

echo "Done."

docker logout "${TARGET_REGISTRY}" || true
rm -f ~/.docker/config.json